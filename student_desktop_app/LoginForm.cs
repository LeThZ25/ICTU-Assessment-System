using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace student_desktop_app
{
    public class LoginForm : Form
    {
        private TextBox txtEmailOrCode;
        private TextBox txtStudentName;
        private TextBox txtAccessCode;
        private Label lblProviderDetect;
        private Button btnLogin;
        private Button btnMockGoogle;
        private Button btnMockMicrosoft;
        private Label lblStatus;
        private readonly ApiClient _api;

        public LoginForm()
        {
            _api = new ApiClient();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Hệ Thống Thi & Đánh Giá Sinh Viên ICTU - Safe Exam Client";
            this.Size = new Size(540, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = Color.FromArgb(30, 41, 59)
            };

            var lblLogo = new Label
            {
                Text = "ICTU ASSESSMENT & ANTI-CHEAT",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(20, 18),
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = "Hỗ trợ tài khoản @ictu.edu.vn (Google) & @ms.ictu.edu.vn (Microsoft 365)",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(20, 50),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblLogo);
            pnlHeader.Controls.Add(lblSub);

            // SSO Buttons
            var lblSso = new Label { Text = "Đăng Nhập Nhanh Qua Cổng Trường:", Location = new Point(45, 105), AutoSize = true, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105) };
            
            btnMockGoogle = new Button
            {
                Text = "G Google Workspace (ictu.edu.vn)",
                Location = new Point(45, 128),
                Width = 215,
                Height = 36,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(217, 48, 37),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold)
            };
            btnMockGoogle.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            btnMockGoogle.Click += (s, e) =>
            {
                txtEmailOrCode.Text = "dtc205122001@ictu.edu.vn";
                txtStudentName.Text = "Nguyễn Văn An";
            };

            btnMockMicrosoft = new Button
            {
                Text = " Microsoft 365 (ms.ictu.edu.vn)",
                Location = new Point(270, 128),
                Width = 215,
                Height = 36,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(0, 120, 212),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold)
            };
            btnMockMicrosoft.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            btnMockMicrosoft.Click += (s, e) =>
            {
                txtEmailOrCode.Text = "dtc205122002@ms.ictu.edu.vn";
                txtStudentName.Text = "Trần Thị Bình";
            };

            // Form inputs
            var lblCode = new Label { Text = "Email Trường / Mã Sinh Viên:", Location = new Point(45, 180), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtEmailOrCode = new TextBox { Text = "dtc205122001@ictu.edu.vn", Location = new Point(45, 205), Width = 440, Height = 32 };
            txtEmailOrCode.TextChanged += TxtEmailOrCode_TextChanged;

            lblProviderDetect = new Label
            {
                Text = "✓ Nhận diện: Google Workspace ICTU",
                ForeColor = Color.FromArgb(16, 185, 129),
                Location = new Point(45, 240),
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold)
            };

            var lblName = new Label { Text = "Họ và Tên Sinh Viên:", Location = new Point(45, 268), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtStudentName = new TextBox { Text = "Nguyễn Văn An", Location = new Point(45, 292), Width = 440, Height = 32 };

            var lblAccess = new Label { Text = "Mã Phòng Thi (Access Code):", Location = new Point(45, 335), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtAccessCode = new TextBox { Text = "ICTU2026", Location = new Point(45, 360), Width = 440, Height = 32, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };

            lblStatus = new Label
            {
                Text = "Cơ chế chống gian lận: Alt+Tab Hook, Kiosk Fullscreen, Cấm Copy/Paste.",
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(45, 405),
                Width = 440,
                Height = 35,
                Font = new Font("Segoe UI", 8F)
            };

            btnLogin = new Button
            {
                Text = "VÀO PHÒNG THI AN TOÀN (FULLSCREEN)",
                Location = new Point(45, 450),
                Width = 440,
                Height = 48,
                BackColor = Color.FromArgb(79, 70, 229),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.Click += BtnLogin_Click;

            this.Controls.Add(pnlHeader);
            this.Controls.Add(lblSso);
            this.Controls.Add(btnMockGoogle);
            this.Controls.Add(btnMockMicrosoft);
            this.Controls.Add(lblCode);
            this.Controls.Add(txtEmailOrCode);
            this.Controls.Add(lblProviderDetect);
            this.Controls.Add(lblName);
            this.Controls.Add(txtStudentName);
            this.Controls.Add(lblAccess);
            this.Controls.Add(txtAccessCode);
            this.Controls.Add(lblStatus);
            this.Controls.Add(btnLogin);
        }

        private void TxtEmailOrCode_TextChanged(object? sender, EventArgs e)
        {
            var text = txtEmailOrCode.Text.Trim().ToLower();
            if (text.EndsWith("@ms.ictu.edu.vn"))
            {
                lblProviderDetect.Text = "✓ Nhận diện: Microsoft 365 ICTU (@ms.ictu.edu.vn)";
                lblProviderDetect.ForeColor = Color.FromArgb(0, 120, 212);
            }
            else if (text.EndsWith("@ictu.edu.vn"))
            {
                lblProviderDetect.Text = "✓ Nhận diện: Google Workspace ICTU (@ictu.edu.vn)";
                lblProviderDetect.ForeColor = Color.FromArgb(16, 185, 129);
            }
            else
            {
                lblProviderDetect.Text = "• Nhập mã SV hoặc email trường ICTU";
                lblProviderDetect.ForeColor = Color.FromArgb(100, 116, 139);
            }
        }

        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            var input = txtEmailOrCode.Text.Trim();
            var studentName = txtStudentName.Text.Trim();
            var accessCode = txtAccessCode.Text.Trim();

            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(studentName))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ Email/Mã SV và Họ tên.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Extract pure StudentCode from email or string
            string studentCode = input;
            if (studentCode.Contains("@"))
            {
                studentCode = studentCode.Split('@')[0].ToUpper();
            }
            else
            {
                studentCode = studentCode.ToUpper();
            }

            btnLogin.Enabled = false;
            lblStatus.Text = "Đang kết nối API và lấy dữ liệu đề thi...";
            lblStatus.ForeColor = Color.FromArgb(79, 70, 229);

            var startRes = await _api.StartExamAsync(1, studentCode, studentName, accessCode);
            if (startRes != null && startRes.Success)
            {
                lblStatus.Text = "Vào thi thành công! Đang kích hoạt môi trường thi chống gian lận...";
                this.Hide();

                var examForm = new ExamForm(_api, startRes, studentCode, studentName);
                examForm.FormClosed += (s, args) => this.Close();
                examForm.Show();
            }
            else
            {
                lblStatus.Text = "Không thể vào phòng thi. Kiểm tra máy chủ hoặc mã phòng thi.";
                lblStatus.ForeColor = Color.FromArgb(225, 29, 72);
                btnLogin.Enabled = true;
            }
        }
    }
}
