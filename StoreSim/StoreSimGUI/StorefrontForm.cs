using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using StoreSim;

namespace StoreSimGUI
{
    public partial class StorefrontForm : Form
    {
        private readonly Store _store;
        private readonly int _customerId;

        // UI Controls
        private DataGridView dgvProducts = null!;
        private ComboBox cmbCategories = null!;
        private Label lblCategory = null!;
        private TextBox txtQuantity = null!;
        private Label lblQuantity = null!;
        private Button btnAddToCart = null!;
        private Button btnGoToCheckout = null!;

        // SQA Fault Injection & Testing Panel Controls
        private GroupBox grpSQAPanel = null!;
        private CheckBox chkSimulateDrop = null!;
        private TrackBar trackBarLatency = null!;
        private Label lblLatencyTitle = null!;
        private Label lblLatencyValue = null!;
        private Button btnRunLoadTest = null!;
        private Label lblStatus = null!;

        public StorefrontForm(Store store, int customerId)
        {
            _store = store;
            _customerId = customerId;

            InitializeComponentCustom();
            LoadCategories();
            LoadProducts();
        }

        private void InitializeComponentCustom()
        {
            this.SuspendLayout();

            this.Text = $"Storefront Simulation - Customer ID: {_customerId}";
            this.Size = new Size(1600, 940);
            this.MinimumSize = new Size(1600, 940);
            this.MaximumSize = new Size(1600, 940);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            int leftMargin = 40;

            lblCategory = new Label
            {
                Text = "Filter by Category:",
                Location = new Point(leftMargin, 20),
                Size = new Size(320, 35)
            };
            lblCategory.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            this.Controls.Add(lblCategory);

            cmbCategories = new ComboBox
            {
                Location = new Point(leftMargin, 62),
                Size = new Size(320, 30),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbCategories.SelectedIndexChanged += cmbCategories_SelectedIndexChanged;
            this.Controls.Add(cmbCategories);

            // Products DataGridView
            dgvProducts = new DataGridView
            {
                Location = new Point(leftMargin, 115),
                Size = new Size(960, 740),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true
            };
            this.Controls.Add(dgvProducts);

            int rightColumnX = 1040;

            // Quantity Label & Box
            lblQuantity = new Label
            {
                Text = "Quantity:",
                Location = new Point(rightColumnX, 20),
                Size = new Size(200, 35)
            };
            lblQuantity.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            this.Controls.Add(lblQuantity);

            txtQuantity = new TextBox
            {
                Location = new Point(rightColumnX, 62),
                Size = new Size(160, 30),
                Text = "1"
            };
            this.Controls.Add(txtQuantity);

            // 4. Action Buttons
            btnAddToCart = new Button
            {
                Text = "Add to Cart",
                Location = new Point(rightColumnX, 115),
                Size = new Size(240, 50),
                UseVisualStyleBackColor = true
            };
            btnAddToCart.Click += btnAddToCart_Click;
            this.Controls.Add(btnAddToCart);

            btnGoToCheckout = new Button
            {
                Text = "View Cart & Checkout",
                Location = new Point(rightColumnX + 260, 115),
                Size = new Size(260, 50),
                UseVisualStyleBackColor = true
            };
            btnGoToCheckout.Click += btnGoToCheckout_Click;
            this.Controls.Add(btnGoToCheckout);

            // SQA Fault Injection & Testing Panel
            grpSQAPanel = new GroupBox
            {
                Text = " SQA Fault Injection & Load Testing Suite ",
                Location = new Point(rightColumnX, 190),
                Size = new Size(520, 665)
            };
            grpSQAPanel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            this.Controls.Add(grpSQAPanel);

            // Checkbox: Connection Drop
            chkSimulateDrop = new CheckBox
            {
                Text = "Simulate Connection Drop (Failures)",
                Location = new Point(25, 45),
                Size = new Size(470, 32)
            };
            chkSimulateDrop.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            chkSimulateDrop.CheckedChanged += ChkSimulateDrop_CheckedChanged;
            grpSQAPanel.Controls.Add(chkSimulateDrop);

            // Artificial Network Latency
            lblLatencyTitle = new Label
            {
                Text = "Artificial Network Latency:",
                Location = new Point(25, 110),
                Size = new Size(350, 28)
            };
            lblLatencyTitle.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            grpSQAPanel.Controls.Add(lblLatencyTitle);

            lblLatencyValue = new Label
            {
                Text = "0 ms",
                Location = new Point(390, 110),
                Size = new Size(100, 28)
            };
            lblLatencyValue.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            grpSQAPanel.Controls.Add(lblLatencyValue);

            trackBarLatency = new TrackBar
            {
                Minimum = 0,
                Maximum = 5000,
                TickFrequency = 500,
                Location = new Point(25, 145),
                Size = new Size(470, 45)
            };
            trackBarLatency.Scroll += TrackBarLatency_Scroll;
            grpSQAPanel.Controls.Add(trackBarLatency);

            // Load Test Button (600 Users)
            btnRunLoadTest = new Button
            {
                Text = "Run 600-User Concurrency Test",
                Location = new Point(25, 225),
                Size = new Size(470, 60),
                UseVisualStyleBackColor = true
            };
            btnRunLoadTest.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            btnRunLoadTest.Click += BtnRunLoadTest_Click;
            grpSQAPanel.Controls.Add(btnRunLoadTest);

            // Status Label
            lblStatus = new Label
            {
                Text = "System Status: Normal Operations",
                ForeColor = Color.Green,
                Location = new Point(25, 315),
                Size = new Size(470, 320)
            };
            lblStatus.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grpSQAPanel.Controls.Add(lblStatus);

            this.ResumeLayout(false);
        }

        private void LoadCategories()
        {
            var products = _store.GetAllProducts();
            var categories = new HashSet<string> { "All" };
            foreach (var p in products)
            {
                if (!string.IsNullOrEmpty(p.Category)) categories.Add(p.Category);
            }
            cmbCategories.DataSource = new List<string>(categories);
        }

        private void LoadProducts(string categoryFilter = "All")
        {
            var products = _store.GetAllProducts();
            if (categoryFilter != "All")
            {
                products = products.FindAll(p => p.Category == categoryFilter);
            }

            dgvProducts.DataSource = null;
            dgvProducts.DataSource = products;
        }

        private void cmbCategories_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbCategories.SelectedItem is string selectedCategory)
            {
                LoadProducts(selectedCategory);
            }
        }

        private void ChkSimulateDrop_CheckedChanged(object sender, EventArgs e)
        {
            Store.SimulateConnectionDrop = chkSimulateDrop.Checked;
            if (chkSimulateDrop.Checked)
            {
                lblStatus.Text = "System Status: CONNECTION DROP INJECTED\n\n(NpgsqlExceptions are intercepted safely without crashing runtime)";
                lblStatus.ForeColor = Color.Red;
            }
            else
            {
                _store.ResetConnectionPools();
                lblStatus.Text = "System Status: Recovered & Normal Operations\n\nConnection pools re-established.";
                lblStatus.ForeColor = Color.Green;
            }
        }

        private void TrackBarLatency_Scroll(object sender, EventArgs e)
        {
            Store.SimulatedLatencyMs = trackBarLatency.Value;
            lblLatencyValue.Text = $"{Store.SimulatedLatencyMs} ms";
        }

        private void BtnRunLoadTest_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            lblStatus.Text = "System Status: Running 600-user load test workload...\nPlease wait.";
            lblStatus.ForeColor = Color.Blue;
            Refresh();

            try
            {
                Store.RunConcurrencyLoadTest(600);
                MessageBox.Show("Load test completed successfully for 600 virtual users without crashing client runtime!",
                    "Load Test Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                lblStatus.Text = "System Status: Load Test Passed Successfully\n\nHandled 600 concurrent virtual users.";
                lblStatus.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Load test error: {ex.Message}", "Test Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = $"System Status: Exception Caught\n\n{ex.Message}";
                lblStatus.ForeColor = Color.Red;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void btnAddToCart_Click(object sender, EventArgs e)
        {
            if (dgvProducts.CurrentRow?.DataBoundItem is Product selectedProduct)
            {
                if (int.TryParse(txtQuantity.Text, out int qty) && qty > 0)
                {
                    bool success = _store.ReserveItem(_customerId, selectedProduct.Id, qty);
                    if (success)
                    {
                        MessageBox.Show($"Added {qty} item(s) of {selectedProduct.Name} to cart.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        LoadProducts(cmbCategories.SelectedItem?.ToString() ?? "All");
                    }
                    else
                    {
                        MessageBox.Show("Reservation failed. Check stock levels or connection status.", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("Please enter a valid numeric quantity.", "Invalid Quantity", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                MessageBox.Show("Please select a product from the table.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnGoToCheckout_Click(object sender, EventArgs e)
        {
            CheckoutForm checkoutForm = new CheckoutForm(_store, _customerId);
            checkoutForm.ShowDialog();
            LoadProducts(cmbCategories.SelectedItem?.ToString() ?? "All");
        }
    }
}