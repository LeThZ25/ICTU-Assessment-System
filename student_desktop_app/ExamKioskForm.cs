using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace student_desktop_app
{
    public class ExamKioskForm : Form
    {
        private WebView2? _webView;
        private Panel? _fallbackPanel;
        private TextBox? _txtServerUrl;
        private Label? _lblFallbackStatus;
        private Button? _btnRetry;

        private System.Windows.Forms.Timer? _processScanTimer;
        private readonly HashSet<string> _reportedProcesses = new();
        private readonly string[] _blacklistedProcesses = new[] { "anydesk", "teamviewer", "ultraviewer", "discord", "obs64" };

        private int _violationCount = 0;
        private int _activeSessionId = 0;
        private string _activeStudentCode = "DTC235210069";
        private string _activeStudentName = "Phạm Hồng Ngọc Kiên";
        private bool _isExamSecurityActive = false;
        private bool _isInternalDialogOpen = false;
        private bool _isFormFullyLoaded = false;
        private DateTime _lastDeactivateViolation = DateTime.MinValue;

        private readonly ApiClient _api;
        private string _targetUrl;

        // Win32 Low-Level Keyboard Hook
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc? _hookProc;
        private static IntPtr _hookId = IntPtr.Zero;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static ExamKioskForm? _currentInstance;

        public ExamKioskForm(
            string serverUrl = "http://localhost:5000/student_web.html?mode=desktop_kiosk",
            string studentCode = "DTC235210069",
            string studentName = "Phạm Hồng Ngọc Kiên")
        {
            _targetUrl = serverUrl;
            _activeStudentCode = string.IsNullOrWhiteSpace(studentCode) ? "DTC235210069" : studentCode;
            _activeStudentName = string.IsNullOrWhiteSpace(studentName) ? "Phạm Hồng Ngọc Kiên" : studentName;
            _api = new ApiClient("http://localhost:5000");

            ConfigureKioskWindow();
            InitializeComponent();
            SetupKeyboardHook();
            SetupProcessScanner();
        }

        private void ConfigureKioskWindow()
        {
            // Khởi đầu ở chế độ cửa sổ bình thường khi xem Bảng tin / Thời khóa biểu / Hồ sơ.
            // Chỉ bật Fullscreen không viền + Anti-Cheat khi sinh viên bấm vào chức năng Kiểm tra / Thi.
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = false;
            this.KeyPreview = true;
            this.BackColor = Color.FromArgb(0, 51, 102);
            this.Text = "Hệ Thống Quản Lý Học Tập & Khảo Thí Thông Minh";

            this.Shown += (s, e) => { _isFormFullyLoaded = true; };

            this.Deactivate += (s, e) =>
            {
                if (!_isFormFullyLoaded || _isInternalDialogOpen || !_isExamSecurityActive) return;

                try
                {
                    IntPtr fgWindow = GetForegroundWindow();
                    if (fgWindow == this.Handle || Form.ActiveForm == this)
                        return;
                }
                catch { }

                if ((DateTime.UtcNow - _lastDeactivateViolation).TotalSeconds < 6) return;
                _lastDeactivateViolation = DateTime.UtcNow;

                TriggerViolation("WindowDeactivated (Alt+Tab)", "Thí sinh mất tiêu điểm cửa sổ trong chế độ kiểm tra an toàn");
            };

            this.FormClosing += (s, e) =>
            {
                CleanupTimersAndHook();
            };
        }

        private void SetExamSecurityMode(bool enable)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => SetExamSecurityMode(enable)));
                return;
            }

            _isExamSecurityActive = enable;
            if (enable)
            {
                this.Text = "Phòng Thi An Toàn - Chế Độ Khóa Màn Hình & Chống Gian Lận";
                this.WindowState = FormWindowState.Normal;
                this.FormBorderStyle = FormBorderStyle.None;
                this.TopMost = true;
                this.WindowState = FormWindowState.Maximized;
                _processScanTimer?.Start();
            }
            else
            {
                this.Text = "Hệ Thống Quản Lý Học Tập & Khảo Thí Thông Minh";
                _processScanTimer?.Stop();
                this.TopMost = false;
                this.FormBorderStyle = FormBorderStyle.Sizable;
            }
        }

        private void InitializeComponent()
        {
            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252)
            };
            this.Controls.Add(_webView);

            BuildFallbackPanel();
            _ = InitializeWebViewAsync();
        }

        private void BuildFallbackPanel()
        {
            _fallbackPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0, 51, 102),
                Visible = false
            };

            var centerCard = new Panel
            {
                Width = 520,
                Height = 360,
                BackColor = Color.White,
                Padding = new Padding(30)
            };
            centerCard.Location = new Point((this.ClientSize.Width - centerCard.Width) / 2, (this.ClientSize.Height - centerCard.Height) / 2);

            _fallbackPanel.Resize += (s, e) =>
            {
                centerCard.Location = new Point((_fallbackPanel.Width - centerCard.Width) / 2, (_fallbackPanel.Height - centerCard.Height) / 2);
            };

            var picLogo = new PictureBox
            {
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point((centerCard.Width - 64) / 2, 20),
                BackColor = Color.Transparent
            };
            if (File.Exists("ictu_logo.png"))
            {
                picLogo.Image = Image.FromFile("ictu_logo.png");
            }

            var lblTitle = new Label
            {
                Text = "HỆ THỐNG HỌC TẬP & KHẢO THÍ THÔNG MINH",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 51, 102),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 95),
                Size = new Size(500, 30)
            };

            _lblFallbackStatus = new Label
            {
                Text = "Chưa kết nối được tới máy chủ khảo thí.\nVui lòng kiểm tra lại dịch vụ Backend đang chạy trên cổng 5000.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(100, 116, 139),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 130),
                Size = new Size(500, 40)
            };

            var lblUrl = new Label
            {
                Text = "Địa chỉ máy chủ (Server URL):",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(40, 185),
                AutoSize = true
            };

            _txtServerUrl = new TextBox
            {
                Text = _targetUrl,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(40, 210),
                Size = new Size(440, 32)
            };

            _btnRetry = new Button
            {
                Text = "KẾT NỐI LẠI HỆ THỐNG LMS",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 74, 153),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(40, 260),
                Size = new Size(440, 42),
                Cursor = Cursors.Hand
            };
            _btnRetry.FlatAppearance.BorderSize = 0;
            _btnRetry.Click += async (s, e) =>
            {
                _targetUrl = _txtServerUrl.Text.Trim();
                _lblFallbackStatus.Text = "Đang kết nối lại tới máy chủ...";
                _lblFallbackStatus.ForeColor = Color.FromArgb(0, 74, 153);
                _btnRetry.Enabled = false;

                await Task.Delay(500);
                if (_webView != null && _webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Navigate(_targetUrl);
                }
                else
                {
                    await InitializeWebViewAsync();
                }
                _btnRetry.Enabled = true;
            };

            centerCard.Controls.Add(picLogo);
            centerCard.Controls.Add(lblTitle);
            centerCard.Controls.Add(_lblFallbackStatus);
            centerCard.Controls.Add(lblUrl);
            centerCard.Controls.Add(_txtServerUrl);
            centerCard.Controls.Add(_btnRetry);

            _fallbackPanel.Controls.Add(centerCard);
            this.Controls.Add(_fallbackPanel);
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                // Create custom user data folder in Temp to isolate exam session
                string userDataFolder = Path.Combine(Path.GetTempPath(), "ICTU_Exam_WebView2_Profile");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                await _webView!.EnsureCoreWebView2Async(env);

                // Security Hardening: Lockdown browser settings
                var settings = _webView.CoreWebView2.Settings;
                settings.AreDevToolsEnabled = false;               // Block F12 / Inspect
                settings.AreDefaultContextMenusEnabled = false;     // Block right-click context menu
                settings.IsStatusBarEnabled = false;                // Hide bottom url status
                settings.AreBrowserAcceleratorKeysEnabled = false;  // Block browser shortcuts (Ctrl+R, F5, Ctrl+P)
                settings.IsBuiltInErrorPageEnabled = false;

                // Block any external popups / new window attempts
                _webView.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true; // Block opening outside windows
                };

                // Bridge communication from Web to Native C#
                _webView.CoreWebView2.WebMessageReceived += HandleWebMessageReceived;

                _webView.NavigationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        if (_fallbackPanel != null) _fallbackPanel.Visible = false;
                        _webView.Visible = true;
                    }
                    else
                    {
                        ShowFallbackScreen($"Không thể tải cổng LMS: {e.WebErrorStatus}. Vui lòng kiểm tra lại Backend API (cổng 5000).");
                    }
                };

                _webView.CoreWebView2.Navigate(_targetUrl);
            }
            catch (Exception ex)
            {
                ShowFallbackScreen($"Lỗi khởi tạo nhân trình duyệt: {ex.Message}");
            }
        }

        private void ShowFallbackScreen(string msg)
        {
            if (_lblFallbackStatus != null)
            {
                _lblFallbackStatus.Text = msg;
                _lblFallbackStatus.ForeColor = Color.FromArgb(225, 29, 72);
            }
            if (_webView != null) _webView.Visible = false;
            if (_fallbackPanel != null) _fallbackPanel.Visible = true;
        }

        private void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(raw)) return;

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("action", out var actionElem))
                {
                    string action = actionElem.GetString() ?? "";
                    if (action == "exit")
                    {
                        CleanupTimersAndHook();
                        this.Close();
                    }
                    else if (action == "proctorExit")
                    {
                        HandleProctorExit();
                    }
                    else if (action == "enableExamMode")
                    {
                        SetExamSecurityMode(true);
                    }
                    else if (action == "disableExamMode")
                    {
                        if (_activeSessionId == 0)
                        {
                            SetExamSecurityMode(false);
                        }
                    }
                    else if (action == "examStarted")
                    {
                        if (doc.RootElement.TryGetProperty("sessionId", out var sidElem) && sidElem.TryGetInt32(out int sid))
                            _activeSessionId = sid;
                        if (doc.RootElement.TryGetProperty("studentCode", out var scElem))
                            _activeStudentCode = scElem.GetString() ?? _activeStudentCode;
                        if (doc.RootElement.TryGetProperty("studentName", out var snElem))
                            _activeStudentName = snElem.GetString() ?? _activeStudentName;
                        _violationCount = 0;
                        SetExamSecurityMode(true);
                    }
                    else if (action == "examEnded")
                    {
                        _activeSessionId = 0;
                        SetExamSecurityMode(false);
                    }
                    else if (action == "updatePassword")
                    {
                        string code = doc.RootElement.TryGetProperty("studentCode", out var cEl) ? (cEl.GetString() ?? "") : "";
                        string newPwd = doc.RootElement.TryGetProperty("newPassword", out var pEl) ? (pEl.GetString() ?? "") : "";
                        if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(newPwd))
                        {
                            SaveLocalStudentPassword(code.Trim().ToUpperInvariant(), newPwd.Trim());
                        }
                    }
                }
            }
            catch { }
        }

        private static void SaveLocalStudentPassword(string studentCode, string newPassword)
        {
            try
            {
                string filePath = Path.Combine(Path.GetTempPath(), "ICTU_Student_Passwords.json");
                Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(filePath))
                {
                    string raw = File.ReadAllText(filePath);
                    var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
                    if (existing != null)
                    {
                        foreach (var kv in existing) map[kv.Key] = kv.Value;
                    }
                }
                map[studentCode] = newPassword;
                File.WriteAllText(filePath, JsonSerializer.Serialize(map));
            }
            catch { }
        }

        private void SetupKeyboardHook()
        {
            _currentInstance = this;
            _hookProc = HookCallback;
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                if (curModule != null)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(curModule.ModuleName!), 0);
                }
            }
        }

        private void SetupProcessScanner()
        {
            _processScanTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            _processScanTimer.Tick += (s, e) =>
            {
                if (!_isExamSecurityActive) return;
                try
                {
                    var running = Process.GetProcesses();
                    foreach (var p in running)
                    {
                        string pName = p.ProcessName.ToLower();
                        foreach (var black in _blacklistedProcesses)
                        {
                            if (pName.Contains(black) && !_reportedProcesses.Contains(pName))
                            {
                                _reportedProcesses.Add(pName);
                                TriggerViolation($"BlacklistedProcess ({pName})", $"Phát hiện phần mềm chia sẻ màn hình/điều khiển từ xa: {p.ProcessName}");
                            }
                        }
                    }
                }
                catch { }
            };
        }

        private void CleanupTimersAndHook()
        {
            _processScanTimer?.Stop();
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (_currentInstance != null && _currentInstance._isExamSecurityActive &&
                nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int flags = Marshal.ReadInt32(lParam, 8);
                bool altDown = (flags & 0x20) != 0;

                // Block Alt+Tab (9), Alt+Esc (27), Win (0x5B, 0x5C), Alt+F4 (0x73) only when in exam mode
                if ((vkCode == 9 && altDown) || (vkCode == 27 && altDown) || vkCode == 0x5B || vkCode == 0x5C || (vkCode == 0x73 && altDown))
                {
                    _currentInstance?.TriggerViolation("SystemKeyBlocked (Win/Alt+Tab)", "Chặn phím tắt chuyển màn hình hoặc thoát ứng dụng");
                    return (IntPtr)1; // Swallow key event
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public async void TriggerViolation(string type, string details)
        {
            _violationCount++;
            System.Media.SystemSounds.Beep.Play();

            try
            {
                if (_webView != null && _webView.CoreWebView2 != null)
                {
                    string jsonMsg = JsonSerializer.Serialize(new
                    {
                        eventType = "nativeViolation",
                        violationCount = _violationCount,
                        type = type,
                        details = details
                    });
                    _webView.CoreWebView2.PostWebMessageAsJson(jsonMsg);
                }
            }
            catch { }

            await _api.LogViolationAsync(_activeSessionId, _activeStudentCode, _activeStudentName, type, details);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Supervisor / Proctor Emergency Exit: Ctrl + Shift + F12
            if (keyData == (Keys.Control | Keys.Shift | Keys.F12))
            {
                HandleProctorExit();
                return true;
            }

            // Block Alt+F4 only when exam security is active
            if (_isExamSecurityActive && keyData == (Keys.Alt | Keys.F4))
            {
                TriggerViolation("Alt+F4 Attempt", "Cố tình đóng ứng dụng phòng thi");
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void HandleProctorExit()
        {
            _isInternalDialogOpen = true;
            try
            {
                using var pinDialog = new Form
                {
                    Text = "Xác Thực Giám Thị Phòng Thi - ICTU",
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterScreen,
                    Width = 380,
                    Height = 200,
                    TopMost = true,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    BackColor = Color.FromArgb(248, 250, 252)
                };

                var lblPrompt = new Label
                {
                    Text = "Nhập mã PIN Giám thị để thoát khẩn cấp:",
                    Left = 24,
                    Top = 20,
                    Width = 320,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold)
                };
                var txtPin = new TextBox
                {
                    Left = 24,
                    Top = 50,
                    Width = 315,
                    PasswordChar = '●',
                    Font = new Font("Segoe UI", 11F)
                };
                var btnOk = new Button
                {
                    Text = "Xác nhận",
                    Left = 145,
                    Top = 95,
                    Width = 90,
                    Height = 34,
                    BackColor = Color.FromArgb(0, 74, 153),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    DialogResult = DialogResult.OK
                };
                btnOk.FlatAppearance.BorderSize = 0;
                pinDialog.Controls.Add(lblPrompt);
                pinDialog.Controls.Add(txtPin);
                pinDialog.Controls.Add(btnOk);
                pinDialog.AcceptButton = btnOk;

                if (pinDialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (txtPin.Text.Trim() == "ICTU2026")
                    {
                        CleanupTimersAndHook();
                        this.Close();
                    }
                    else
                    {
                        MessageBox.Show(this, "Mã PIN không đúng! Thao tác thoát bị từ chối.", "Từ chối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            finally
            {
                _isInternalDialogOpen = false;
            }
        }
    }
}
