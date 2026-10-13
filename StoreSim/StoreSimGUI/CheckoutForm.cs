using System;
using System.Drawing;
using System.Windows.Forms;
using StoreSim;

namespace StoreSimGUI
{
    public partial class CheckoutForm : Form
    {
        private readonly Store _store;
        private readonly int _customerId;

        // UI Controls matching StorefrontForm standards
        private DataGridView dgvCart = null!;
        private Label lblCartTitle = null!;
        private Label lblTotalAmount = null!;
        private Button btnCompleteCheckout = null!;
        private Button btnRemoveItem = null!;
        private Button btnBackToStorefront = null!;

        public CheckoutForm(Store store, int customerId)
        {
            _store = store;
            _customerId = customerId;

            InitializeComponentCustom();
            LoadCartItems();
        }

        private void InitializeComponentCustom()
        {
            this.SuspendLayout();

            // Form Settings - Matching standard 1600x940 layout
            this.Text = $"Checkout Simulation - Customer ID: {_customerId}";
            this.Size = new Size(1600, 940);
            this.MinimumSize = new Size(1600, 940);
            this.MaximumSize = new Size(1600, 940);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;

            int leftMargin = 40;

            lblCartTitle = new Label
            {
                Text = "Your Shopping Cart & Checkout Summary:",
                Location = new Point(leftMargin, 20),
                Size = new Size(500, 35)
            };
            lblCartTitle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            this.Controls.Add(lblCartTitle);

            // Cart DataGridView
            dgvCart = new DataGridView
            {
                Location = new Point(leftMargin, 75),
                Size = new Size(960, 700),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true
            };
            this.Controls.Add(dgvCart);


            int rightColumnX = 1040;

            //  Total Amount Label
            lblTotalAmount = new Label
            {
                Text = "Total Amount: $0.00",
                Location = new Point(rightColumnX, 75),
                Size = new Size(450, 32)
            };
            lblTotalAmount.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            this.Controls.Add(lblTotalAmount);

            // Complete Checkout Button
            btnCompleteCheckout = new Button
            {
                Text = "Complete Purchase",
                Location = new Point(rightColumnX, 140),
                Size = new Size(240, 55),
                UseVisualStyleBackColor = true
            };
            btnCompleteCheckout.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnCompleteCheckout.Click += BtnCompleteCheckout_Click;
            this.Controls.Add(btnCompleteCheckout);

            // Remove Selected Item Button (Releases lock back to inventory)
            btnRemoveItem = new Button
            {
                Text = "Remove Selected Item",
                Location = new Point(rightColumnX + 260, 140),
                Size = new Size(240, 55),
                UseVisualStyleBackColor = true
            };
            btnRemoveItem.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            btnRemoveItem.Click += BtnRemoveItem_Click;
            this.Controls.Add(btnRemoveItem);

            // 6. Back to Storefront Navigation Button (Moved down slightly or arranged cleanly)
            btnBackToStorefront = new Button
            {
                Text = "Back to Storefront",
                Location = new Point(rightColumnX, 215),
                Size = new Size(500, 50),
                UseVisualStyleBackColor = true
            };
            btnBackToStorefront.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            btnBackToStorefront.Click += BtnBackToStorefront_Click;
            this.Controls.Add(btnBackToStorefront);

            this.ResumeLayout(false);
        }

        private void LoadCartItems()
        {
            try
            {
                var cartItems = _store.GetCustomerCart(_customerId);

                dgvCart.DataSource = null;
                dgvCart.DataSource = cartItems;

                decimal grandTotal = 0.0m;
                foreach (var item in cartItems)
                {
                    grandTotal += item.Total;
                }

                lblTotalAmount.Text = $"Total Amount: ${grandTotal:F2}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading cart items: {ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnRemoveItem_Click(object sender, EventArgs e)
        {
            if (dgvCart.CurrentRow?.DataBoundItem is CartDisplayItem selectedItem)
            {
                string input = Microsoft.VisualBasic.Interaction.InputBox(
                    $"Enter quantity to remove for {selectedItem.Name} (Max {selectedItem.Quantity}):",
                    "Remove Item",
                    selectedItem.Quantity.ToString());

                if (int.TryParse(input, out int qtyToRemove) && qtyToRemove > 0)
                {
                    if (qtyToRemove > selectedItem.Quantity)
                    {
                        MessageBox.Show("Cannot remove more than the quantity in your cart.", "Invalid Quantity", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Calls backend to return stock to the database and update cart
                    bool success = _store.ReturnItemToInventory(_customerId, selectedItem.ProductId, qtyToRemove);
                    if (success)
                    {
                        MessageBox.Show($"Successfully removed {qtyToRemove} item(s). Stock released back to inventory.", "Item Removed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        LoadCartItems(); // Refresh view and total
                    }
                    else
                    {
                        MessageBox.Show("Failed to return item to inventory. Check connection status.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select an item from your cart table to remove.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnCompleteCheckout_Click(object sender, EventArgs e)
        {
            try
            {
                bool success = _store.CompleteCheckout(_customerId);
                if (success)
                {
                    MessageBox.Show("Checkout completed successfully! Order recorded and cart cleared.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Checkout failed. Your cart may be empty or there was a connection issue.", "Checkout Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Checkout exception caught: {ex.Message}", "Transaction Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnBackToStorefront_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}