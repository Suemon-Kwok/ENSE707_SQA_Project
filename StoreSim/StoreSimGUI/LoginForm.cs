using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using StoreSim;

namespace StoreSimGUI
{
    public partial class LoginForm : Form
    {
        private readonly Store _store;

        // UI Controls
        private Label lblLogoAscii = null!;
        private Label lblStoreName = null!;
        private Label lblSubtitle = null!;
        private Label lblCustomerName = null!;
        private TextBox txtCustomerName = null!;
        private Button btnLoginOrRegister = null!;
        private Label lblStatus = null!;

        public LoginForm(Store store)
        {
            _store = store;
            InitializeComponentCustom();
            LoadAsciiLogo();
        }

        private void InitializeComponentCustom()
        {
            this.SuspendLayout();

            // Form Settings
            this.Text = "Tabletop Haven - Secure Session Entry";
            this.Size = new Size(1600, 940);
            this.MinimumSize = new Size(1600, 940);
            this.MaximumSize = new Size(1600, 940);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;

            int startY = 40;

            // ASCII Art Logo Display
            lblLogoAscii = new Label
            {
                Location = new Point(350, startY),
                Size = new Size(900, 200),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = this.BackColor
            };
            lblLogoAscii.Font = new Font("Consolas", 9F, FontStyle.Bold);
            this.Controls.Add(lblLogoAscii);

            lblStoreName = new Label
            {
                Text = "Tabletop Haven",
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(350, startY + 205),
                Size = new Size(900, 80)
            };
            lblStoreName.Font = new Font("Segoe UI", 26, FontStyle.Bold);
            this.Controls.Add(lblStoreName);

            // Subtitle
            lblSubtitle = new Label
            {
                Text = "Your premier online destination for hobby games, dice, and miniatures.",
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(350, startY + 285),
                Size = new Size(900, 35)
            };
            lblSubtitle.Font = new Font("Segoe UI", 11, FontStyle.Regular);
            this.Controls.Add(lblSubtitle);

            int inputWidth = 480;
            int inputX = (1600 - inputWidth) / 2;

            // Customer Name Label
            lblCustomerName = new Label
            {
                Text = "Customer / User Name:",
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(inputX, startY + 335),
                Size = new Size(inputWidth, 30)
            };
            lblCustomerName.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            this.Controls.Add(lblCustomerName);

            // Customer Name TextBox
            txtCustomerName = new TextBox
            {
                Location = new Point(inputX, startY + 370),
                Size = new Size(inputWidth, 40)
            };
            txtCustomerName.Font = new Font("Segoe UI", 13, FontStyle.Regular);
            txtCustomerName.Text = string.Empty;
            this.Controls.Add(txtCustomerName);

            // Login / Launch Button
            btnLoginOrRegister = new Button
            {
                Text = "Launch Storefront Session",
                Location = new Point(inputX, startY + 430),
                Size = new Size(inputWidth, 55),
                UseVisualStyleBackColor = true
            };
            btnLoginOrRegister.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            btnLoginOrRegister.Click += BtnLoginOrRegister_Click;
            this.Controls.Add(btnLoginOrRegister);

            // Status Footer Label
            lblStatus = new Label
            {
                Text = "System Status: Ready for database connection & session initialization.",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DarkGreen,
                Location = new Point(350, startY + 505),
                Size = new Size(900, 45)
            };
            lblStatus.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            this.Controls.Add(lblStatus);

            this.ResumeLayout(false);
        }

        private void LoadAsciiLogo()
        {
            try
            {
                string asciiArt = _store.GetStoreLogoAscii();
                if (!string.IsNullOrEmpty(asciiArt))
                {
                    lblLogoAscii.Text = asciiArt;
                }
                else
                {
                    lblLogoAscii.Text = "      [Tabletop Haven Logo Missing]";
                }
            }
            catch
            {
                lblLogoAscii.Text = "      [Tabletop Haven]";
            }
        }

        private void BtnLoginOrRegister_Click(object sender, EventArgs e)
        {
            string customerName = txtCustomerName.Text.Trim();

            if (string.IsNullOrEmpty(customerName))
            {
                MessageBox.Show("Please enter a valid customer name.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                lblStatus.Text = "System Status: Registering/Authenticating customer in PostgreSQL...";
                Refresh();

                int customerId = _store.RegisterCustomer(customerName);

                if (customerId > 0)
                {
                    this.Hide();
                    StorefrontForm storefront = new StorefrontForm(_store, customerId);
                    storefront.ShowDialog();

                    this.Show();
                    txtCustomerName.Clear();
                    lblStatus.Text = "System Status: Session ended. Ready for next login.";
                    lblStatus.ForeColor = Color.DarkGreen;
                }
                else
                {
                    MessageBox.Show("Failed to register customer. Check your database connectivity.", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    lblStatus.Text = "System Status: Connection/Registration Error.";
                    lblStatus.ForeColor = Color.Red;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Login exception caught: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = $"System Status: Exception - {ex.Message}";
                lblStatus.ForeColor = Color.Red;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }
}