using System;
using StoreSim;

Store myStore = new Store();

//checking customer creation db
Console.WriteLine("Registering new customer...");
int customerId = myStore.RegisterCustomer("Jane Doe");
Console.WriteLine($"Customer registered with ID: {customerId}\n");

Console.WriteLine("=== STARTING DATABASE STORE SIMULATION ===");

//checking reserve item functionality
Console.WriteLine($"\n--- Customer {customerId} trying to reserve 2 of Product ID 1 ---");
bool reserve1 = myStore.ReserveItem(customerId, 1, 2);
Console.WriteLine($"Reservation successful? {reserve1}");
Console.WriteLine($"Product 1 remaining stock: {myStore.GetProduct(1)?.Quantity}\n");

// checking checkout functionality
Console.WriteLine($"--- Customer {customerId} completing checkout ---");
bool checkoutSuccess = myStore.CompleteCheckout(customerId);
Console.WriteLine($"Checkout successful? {checkoutSuccess}");

Console.WriteLine("\n=x SIMULATION COMPLETE x=");