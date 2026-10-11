using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Npgsql;

namespace StoreSim
{
    public class Store
    {
        // Explicit toggles for failure states (Connection drop & Latency)
        public static bool SimulateConnectionDrop { get; set; } = false;
        public static int SimulatedLatencyMs { get; set; } = 0;

        private readonly string _connectionString = "Host=localhost;Port=5432;Database=StoreSimDB;Username=postgres;Password=GitGudAdminT3$t";

        private NpgsqlConnection GetConnection()
        {
            if (SimulateConnectionDrop)
            {
                // Simulate connection failure throwing NpgsqlException
                throw new NpgsqlException("Simulated database connection dropped.");
            }

            return new NpgsqlConnection(_connectionString);
        }

        private void ApplyLatency()
        {
            if (SimulatedLatencyMs > 0)
            {
                Thread.Sleep(SimulatedLatencyMs);
            }
        }

        public T ExecuteSafely<T>(Func<NpgsqlConnection, T> operation, T fallbackValue = default)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                ApplyLatency();

                using var conn = GetConnection();
                conn.Open();

                var result = operation(conn);
                stopwatch.Stop();

                // Log performance warning if normal query exceeds 300ms
                if (stopwatch.ElapsedMilliseconds > 300 && !SimulateConnectionDrop)
                {
                    System.Diagnostics.Debug.WriteLine($"[Performance Warning] Query took {stopwatch.ElapsedMilliseconds}ms (>300ms threshold).");
                }

                return result;
            }
            catch (NpgsqlException ex)
            {
                // Intercept 100% of NpgsqlExceptions, capturing timestamp & error code without crashing GUI
                LogErrorToAudit(ex);
                return fallbackValue;
            }
            catch (Exception ex)
            {
                // General safety catch to prevent unhandled WinForms crashes
                System.Diagnostics.Debug.WriteLine($"[Unexpected Error] {DateTime.UtcNow}: {ex.Message}");
                return fallbackValue;
            }
        }

        private void LogErrorToAudit(NpgsqlException ex)
        {
            try
            {
                string logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Error Code: {ex.SqlState} | Message: {ex.Message}";
                System.IO.File.AppendAllText("db_error_audit.log", logEntry + Environment.NewLine);
            }
            catch
            {
                // Fallback if file logging fails
            }
        }

        // Reset pool connection health check & clearing
        public void ResetConnectionPools()
        {
            SimulateConnectionDrop = false;
            SimulatedLatencyMs = 0;
            NpgsqlConnection.ClearAllPools();
        }

        public Product GetProduct(int productId)
        {
            return ExecuteSafely(conn =>
            {
                using var cmd = new NpgsqlCommand("SELECT id, name, description, category, quantity, price FROM products WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("id", productId);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new Product
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Description = reader.GetString(2),
                        Category = reader.GetString(3),
                        Quantity = reader.GetInt32(4),
                        Price = reader.GetDecimal(5)
                    };
                }
                return null;
            });
        }

        public int RegisterCustomer(string name)
        {
            return ExecuteSafely(conn =>
            {
                string query = "INSERT INTO customers (name) VALUES (@name) RETURNING id";
                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("name", name);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }, -1);
        }

        public bool ReserveItem(int customerId, int productId, int quantityForCart)
        {
            return ExecuteSafely(conn =>
            {
                using var transaction = conn.BeginTransaction();
                try
                {
                    int currentStock = 0;
                    using (var checkCmd = new NpgsqlCommand("SELECT quantity FROM products WHERE id = @pId FOR UPDATE", conn, transaction))
                    {
                        checkCmd.Parameters.AddWithValue("pId", productId);
                        var result = checkCmd.ExecuteScalar();
                        if (result == null) return false;
                        currentStock = Convert.ToInt32(result);
                    }

                    if (currentStock < quantityForCart)
                    {
                        transaction.Rollback();
                        return false;
                    }

                    using (var updateCmd = new NpgsqlCommand("UPDATE products SET quantity = quantity - @qty WHERE id = @pId", conn, transaction))
                    {
                        updateCmd.Parameters.AddWithValue("qty", quantityForCart);
                        updateCmd.Parameters.AddWithValue("pId", productId);
                        updateCmd.ExecuteNonQuery();
                    }

                    using (var cartCmd = new NpgsqlCommand(@"
                        INSERT INTO cart_items (customer_id, product_id, quantity) 
                        VALUES (@cId, @pId, @qty)
                        ON CONFLICT (customer_id, product_id) 
                        DO UPDATE SET quantity = cart_items.quantity + @qty", conn, transaction))
                    {
                        cartCmd.Parameters.AddWithValue("cId", customerId);
                        cartCmd.Parameters.AddWithValue("pId", productId);
                        cartCmd.Parameters.AddWithValue("qty", quantityForCart);
                        cartCmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    return true;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }, false);
        }

        public bool ReturnItemToInventory(int customerId, int productId, int quantityToReturn)
        {
            return ExecuteSafely(conn =>
            {
                using var transaction = conn.BeginTransaction();
                try
                {
                    int cartQuantity = 0;
                    using (var checkCart = new NpgsqlCommand("SELECT quantity FROM cart_items WHERE customer_id = @cId AND product_id = @pId", conn, transaction))
                    {
                        checkCart.Parameters.AddWithValue("cId", customerId);
                        checkCart.Parameters.AddWithValue("pId", productId);
                        var result = checkCart.ExecuteScalar();
                        if (result == null) return false;
                        cartQuantity = Convert.ToInt32(result);
                    }

                    if (cartQuantity < quantityToReturn)
                    {
                        transaction.Rollback();
                        return false;
                    }

                    using (var updateProd = new NpgsqlCommand("UPDATE products SET quantity = quantity + @qty WHERE id = @pId", conn, transaction))
                    {
                        updateProd.Parameters.AddWithValue("qty", quantityToReturn);
                        updateProd.Parameters.AddWithValue("pId", productId);
                        updateProd.ExecuteNonQuery();
                    }

                    if (cartQuantity == quantityToReturn)
                    {
                        using var deleteCart = new NpgsqlCommand("DELETE FROM cart_items WHERE customer_id = @cId AND product_id = @pId", conn, transaction);
                        deleteCart.Parameters.AddWithValue("cId", customerId);
                        deleteCart.Parameters.AddWithValue("pId", productId);
                        deleteCart.ExecuteNonQuery();
                    }
                    else
                    {
                        using var updateCart = new NpgsqlCommand("UPDATE cart_items SET quantity = quantity - @qty WHERE customer_id = @cId AND product_id = @pId", conn, transaction);
                        updateCart.Parameters.AddWithValue("qty", quantityToReturn);
                        updateCart.Parameters.AddWithValue("cId", customerId);
                        updateCart.Parameters.AddWithValue("pId", productId);
                        updateCart.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    return true;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }, false);
        }

        public List<Product> GetAllProducts()
        {
            return ExecuteSafely(conn =>
            {
                var products = new List<Product>();
                using var cmd = new NpgsqlCommand("SELECT id, name, description, category, quantity, price FROM products ORDER BY id", conn);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    products.Add(new Product
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Description = reader.GetString(2),
                        Category = reader.GetString(3),
                        Quantity = reader.GetInt32(4),
                        Price = reader.GetDecimal(5)
                    });
                }
                return products;
            }, new List<Product>());
        }

        public bool CompleteCheckout(int customerId)
        {
            return ExecuteSafely(conn =>
            {
                using var transaction = conn.BeginTransaction();
                try
                {
                    int itemCount = 0;
                    using (var check = new NpgsqlCommand("SELECT COUNT(*) FROM cart_items WHERE customer_id = @cId", conn, transaction))
                    {
                        check.Parameters.AddWithValue("cId", customerId);
                        itemCount = Convert.ToInt32(check.ExecuteScalar());
                    }

                    if (itemCount == 0)
                    {
                        transaction.Rollback();
                        return false;
                    }

                    using (var clearCmd = new NpgsqlCommand("DELETE FROM cart_items WHERE customer_id = @cId", conn, transaction))
                    {
                        clearCmd.Parameters.AddWithValue("cId", customerId);
                        clearCmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    return true;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }, false);
        }

        public List<CartDisplayItem> GetCustomerCart(int customerId)
        {
            return ExecuteSafely(conn =>
            {
                var items = new List<CartDisplayItem>();
                string query = @"
                    SELECT ci.product_id, p.name, ci.quantity, p.price 
                    FROM cart_items ci
                    JOIN products p ON ci.product_id = p.id
                    WHERE ci.customer_id = @cId";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("cId", customerId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new CartDisplayItem
                    {
                        ProductId = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Quantity = reader.GetInt32(2),
                        Price = reader.GetDecimal(3)
                    });
                }
                return items;
            }, new List<CartDisplayItem>());
        }

        public bool CancelEntireOrder(int customerId)
        {
            var cartItems = GetCustomerCart(customerId);
            foreach (var item in cartItems)
            {
                ReturnItemToInventory(customerId, item.ProductId, item.Quantity);
            }
            return true;
        }

        //Concurrency Load Simulator (100 to 600 virtual users workload)
        public static void RunConcurrencyLoadTest(int virtualUsers)
        {
            Parallel.For(0, virtualUsers, i =>
            {
                var localStore = new Store();
                try
                {
                    var products = localStore.GetAllProducts();
                    int custId = localStore.RegisterCustomer($"VirtualUser_{i}");
                    if (products.Count > 0 && custId > 0)
                    {
                        localStore.ReserveItem(custId, products[0].Id, 1);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Virtual User {i} error: {ex.Message}");
                }
            });
        }
    }
}