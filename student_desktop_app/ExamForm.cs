using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace student_desktop_app
{
    public class ExamForm : Form
    {
        private readonly ApiClient _api;
        private readonly StartExamResponse _examData;
        private readonly string _studentCode;
        private readonly string _studentName;

        private int _currentIndex = 0;
        private int _timeLeftSeconds = 20 * 60;
        private int _violationCount = 0;
        private float _currentFontSize = 11.0f;
        private bool _isInternalDialogOpen = false;
        private bool _isSubmitted = false;
        private bool _isFormFullyLoaded = false;
        private DateTime _lastDeactivateViolation = DateTime.MinValue;
        private readonly HashSet<string> _reportedProcesses = new();

        private System.Windows.Forms.Timer _examTimer = null!;
        private System.Windows.Forms.Timer _processScanTimer = null!;

        private Dictionary<int, string> _mcqAnswers = new();
        private Dictionary<int, string> _essayAnswers = new();
        private HashSet<int> _flaggedQuestions = new();

        private readonly string[] _blacklistedProcesses = new[] { "anydesk", "teamviewer", "ultraviewer", "discord", "obs64" };

        // Win32 Low-Level Keyboard Hook
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _hookProc = null!;
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

        // UI Controls
        private Label lblExamTitle = null!;
        private Label lblStudentInfo = null!;
        private Label lblTimer = null!;
        private Label lblViolation = null!;
        private FlowLayoutPanel pnlPalette = null!;
        private Label lblQuestionHeader = null!;
        private Label lblQuestionBody = null!;
        private Panel pnlOptions = null!;
        private RadioButton rbA = null!, rbB = null!, rbC = null!, rbD = null!;
        private TextBox txtEssay = null!;
        private Button btnPrev = null!, btnNext = null!, btnFlag = null!, btnSubmit = null!;
        private Button btnZoomIn = null!, btnZoomOut = null!;

        // AI Chat Controls
        private RichTextBox rtbAiChat = null!;
        private TextBox txtAiInput = null!;
        private Button btnAiSend = null!;

        private static ExamForm? _currentInstance;

        public ExamForm(ApiClient api, StartExamResponse examData, string studentCode, string studentName)
        {
            _api = api;
            _examData = examData;
            _studentCode = studentCode;
            _studentName = studentName;
            _timeLeftSeconds = (examData.DurationMinutes > 0 ? examData.DurationMinutes : 20) * 60;

            SetupSafeKiosk();
            InitializeComponent();
            RestoreDraftFromDisk();
            SetupAntiCheat();
            SetupProcessScanner();
            LoadQuestion(0);
            StartCountdown();
        }

        private void SetupSafeKiosk()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true; // Lock on top
            this.KeyPreview = true;
            this.BackColor = Color.FromArgb(241, 245, 249);
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            this.Shown += (s, e) => { _isFormFullyLoaded = true; };
        }

        private void SetupAntiCheat()
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

            this.FormClosing += (s, e) =>
            {
                CleanupTimersAndHook();
            };

            // Detect window lost focus (Alt+Tab, Windows Key, Task Manager)
            // Guard against internal dialogs, startup transitions, and false alarms
            this.Deactivate += async (s, e) =>
            {
                if (!_isFormFullyLoaded || _isInternalDialogOpen || _isSubmitted) return;

                // Check if the foreground window is still this form or active form in the app
                try
                {
                    IntPtr fgWindow = GetForegroundWindow();
                    if (fgWindow == this.Handle || Form.ActiveForm == this)
                        return; // False positive: still inside this window!
                }
                catch { }

                // Rate-limit to at most 1 event per 6 seconds to prevent flooding
                if ((DateTime.UtcNow - _lastDeactivateViolation).TotalSeconds < 6) return;
                _lastDeactivateViolation = DateTime.UtcNow;

                _violationCount++;
                lblViolation.Text = $"CẢNH BÁO: {_violationCount} LẦN VI PHẠM";
                lblViolation.Visible = true;
                System.Media.SystemSounds.Beep.Play();
                await _api.LogViolationAsync(_examData.SessionId, _studentCode, _studentName, "WindowDeactivated (Alt+Tab)", "Thí sinh mất tiêu điểm cửa sổ làm bài");
            };
        }

        private void SetupProcessScanner()
        {
            _processScanTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            _processScanTimer.Tick += async (s, e) =>
            {
                if (!_isFormFullyLoaded || _isSubmitted || _isInternalDialogOpen) return;
                try
                {
                    var processes = Process.GetProcesses();
                    foreach (var p in processes)
                    {
                        try
                        {
                            // Filter out background services (Session 0 or invisible windows)
                            if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle))
                                continue;

                            string pName = p.ProcessName.ToLower();
                            foreach (var black in _blacklistedProcesses)
                            {
                                if (pName == black || pName.StartsWith(black + "_") || pName.StartsWith(black + "."))
                                {
                                    // Alert ONCE per running app to prevent spamming
                                    if (_reportedProcesses.Add(pName))
                                    {
                                        TriggerViolation("BlacklistedProcessDetected", $"Phát hiện ứng dụng gian lận đang mở: {p.ProcessName} ({p.MainWindowTitle})");
                                        return;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            };
            _processScanTimer.Start();
        }

        private void CleanupTimersAndHook()
        {
            _examTimer?.Stop();
            _processScanTimer?.Stop();
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int flags = Marshal.ReadInt32(lParam, 8);
                bool altDown = (flags & 0x20) != 0;

                // Block Alt+Tab (9), Alt+Esc (27), Win (0x5B, 0x5C), Alt+F4 (0x73)
                if ((vkCode == 9 && altDown) || (vkCode == 27 && altDown) || vkCode == 0x5B || vkCode == 0x5C || (vkCode == 0x73 && altDown))
                {
                    _currentInstance?.TriggerViolation("SystemKeyBlocked (Win/Alt+Tab)", "Chặn phím tắt chuyển màn hình hoặc thoát ứng dụng");
                    return (IntPtr)1; // Swallow key
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Supervisor / Proctor Emergency Exit: Ctrl + Shift + F12
            if (keyData == (Keys.Control | Keys.Shift | Keys.F12))
            {
                HandleProctorExit();
                return true;
            }

            // Block Alt+F4
            if (keyData == (Keys.Alt | Keys.F4))
            {
                TriggerViolation("Alt+F4 Attempt", "Cố tình đóng ứng dụng phòng thi");
                return true;
            }
            // Block Ctrl+C (Copy)
            if (keyData == (Keys.Control | Keys.C))
            {
                TriggerViolation("ClipboardCopy Attempt", "Cố ý sao chép nội dung câu hỏi");
                return true;
            }
            // Block Ctrl+V (Paste)
            if (keyData == (Keys.Control | Keys.V))
            {
                TriggerViolation("ClipboardPaste Attempt", "Cố ý dán tài liệu từ bên ngoài");
                return true;
            }
            // Block Ctrl+X
            if (keyData == (Keys.Control | Keys.X))
            {
                TriggerViolation("ClipboardCut Attempt", "Cố ý cắt văn bản");
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
                    Text = "Xác Thực Giám Thị Phòng Thi",
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterScreen,
                    Width = 360,
                    Height = 180,
                    TopMost = true,
                    MaximizeBox = false,
                    MinimizeBox = false
                };

                var lblPrompt = new Label
                {
                    Text = "Nhập mã PIN Giám thị để thoát khẩn cấp:",
                    Left = 20,
                    Top = 20,
                    Width = 300
                };
                var txtPin = new TextBox
                {
                    Left = 20,
                    Top = 48,
                    Width = 300,
                    PasswordChar = '●'
                };
                var btnOk = new Button
                {
                    Text = "Xác nhận",
                    Left = 140,
                    Top = 90,
                    Width = 85,
                    DialogResult = DialogResult.OK
                };
                var btnCancel = new Button
                {
                    Text = "Hủy",
                    Left = 235,
                    Top = 90,
                    Width = 85,
                    DialogResult = DialogResult.Cancel
                };

                pinDialog.Controls.AddRange(new Control[] { lblPrompt, txtPin, btnOk, btnCancel });
                pinDialog.AcceptButton = btnOk;
                pinDialog.CancelButton = btnCancel;

                if (pinDialog.ShowDialog(this) == DialogResult.OK)
                {
                    string pin = txtPin.Text.Trim();
                    if (pin == "ICTU@PROCTOR" || pin == "2026")
                    {
                        CleanupTimersAndHook();
                        _isSubmitted = true;
                        this.Close();
                    }
                    else
                    {
                        MessageBox.Show("Mã PIN Giám thị không chính xác!", "Từ chối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            finally
            {
                _isInternalDialogOpen = false;
            }
        }

        private async void TriggerViolation(string type, string details)
        {
            if (_isInternalDialogOpen || _isSubmitted) return;

            _violationCount++;
            lblViolation.Text = $"CẢNH BÁO: {_violationCount} LẦN VI PHẠM";
            lblViolation.Visible = true;
            System.Media.SystemSounds.Beep.Play();
            await _api.LogViolationAsync(_examData.SessionId, _studentCode, _studentName, type, details);
        }

        private void InitializeComponent()
        {
            // 1. Top Bar
            var topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(15, 23, 42)
            };

            lblExamTitle = new Label
            {
                Text = _examData.ExamTitle,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(20, 12),
                AutoSize = true
            };

            lblStudentInfo = new Label
            {
                Text = $"Thí sinh: {_studentName} ({_studentCode}) • ICTU Safe Exam Kiosk",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 38),
                AutoSize = true
            };

            // Zoom Controls
            btnZoomOut = new Button
            {
                Text = "A−",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(this.Width - 300, 18),
                Size = new Size(35, 28),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnZoomOut.FlatAppearance.BorderSize = 0;
            btnZoomOut.Click += (s, e) => AdjustFontSize(-1.5f);

            btnZoomIn = new Button
            {
                Text = "A+",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(this.Width - 260, 18),
                Size = new Size(35, 28),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnZoomIn.FlatAppearance.BorderSize = 0;
            btnZoomIn.Click += (s, e) => AdjustFontSize(1.5f);

            lblTimer = new Label
            {
                Text = "20:00",
                ForeColor = Color.FromArgb(244, 63, 94),
                Font = new Font("Consolas", 18F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(this.Width - 130, 16),
                AutoSize = true
            };

            lblViolation = new Label
            {
                Text = "CẢNH BÁO: 0 LẦN VI PHẠM",
                ForeColor = Color.FromArgb(254, 202, 202),
                BackColor = Color.FromArgb(159, 18, 57),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(this.Width - 550, 20),
                Padding = new Padding(6, 4, 6, 4),
                AutoSize = true,
                Visible = false
            };

            topBar.Controls.Add(lblExamTitle);
            topBar.Controls.Add(lblStudentInfo);
            topBar.Controls.Add(btnZoomOut);
            topBar.Controls.Add(btnZoomIn);
            topBar.Controls.Add(lblViolation);
            topBar.Controls.Add(lblTimer);

            // 2. Left Navigator Panel (Mục lục & Palette trạng thái 3 màu)
            var leftPnl = new Panel
            {
                Dock = DockStyle.Left,
                Width = 240,
                BackColor = Color.White,
                Padding = new Padding(15)
            };

            var lblIndex = new Label
            {
                Text = "MỤC LỤC CÂU HỎI",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Dock = DockStyle.Top,
                Height = 25
            };

            // Legend chú thích màu sắc
            var pnlLegend = new Panel
            {
                Dock = DockStyle.Top,
                Height = 45,
                Padding = new Padding(0, 0, 0, 8)
            };
            var lblLegend = new Label
            {
                Text = "■ Đã làm   ■ Xem lại   ■ Chưa làm",
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Fill
            };
            pnlLegend.Controls.Add(lblLegend);

            btnSubmit = new Button
            {
                Text = "NỘP BÀI THI",
                Dock = DockStyle.Bottom,
                Height = 45,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSubmit.FlatAppearance.BorderSize = 0;
            btnSubmit.Click += BtnSubmit_Click;

            pnlPalette = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            leftPnl.Controls.Add(pnlPalette);
            leftPnl.Controls.Add(pnlLegend);
            leftPnl.Controls.Add(lblIndex);
            leftPnl.Controls.Add(btnSubmit);

            // 3. Right Socratic Oral Chat Panel
            var rightPnl = new Panel
            {
                Dock = DockStyle.Right,
                Width = 360,
                BackColor = Color.White,
                Padding = new Padding(15)
            };

            var lblAiTitle = new Label
            {
                Text = "AI GIÁM KHẢO VẤN ĐÁP",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(79, 70, 229),
                Dock = DockStyle.Top,
                Height = 25
            };

            var lblAiDesc = new Label
            {
                Text = "Vấn đáp trực tiếp với Tác tử AI để làm rõ lập luận tự luận.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Top,
                Height = 35
            };

            rtbAiChat = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5F)
            };
            rtbAiChat.AppendText("[AI Giám Khảo]: Chào bạn! Hãy làm bài tự tin. Cần vấn đáp làm rõ luận điểm hãy trao đổi tại đây.\n\n");

            var bottomChat = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                Padding = new Padding(0, 8, 0, 0)
            };

            txtAiInput = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F)
            };
            txtAiInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) BtnAiSend_Click(s, e); };

            btnAiSend = new Button
            {
                Text = "Gửi",
                Dock = DockStyle.Right,
                Width = 65,
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnAiSend.FlatAppearance.BorderSize = 0;
            btnAiSend.Click += BtnAiSend_Click;

            bottomChat.Controls.Add(txtAiInput);
            bottomChat.Controls.Add(btnAiSend);

            rightPnl.Controls.Add(rtbAiChat);
            rightPnl.Controls.Add(bottomChat);
            rightPnl.Controls.Add(lblAiDesc);
            rightPnl.Controls.Add(lblAiTitle);

            // 4. Center Main Question Area
            var centerPnl = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(25)
            };

            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(30)
            };

            lblQuestionHeader = new Label
            {
                Text = "Câu hỏi 1 / 5",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(79, 70, 229),
                Dock = DockStyle.Top,
                Height = 30
            };

            lblQuestionBody = new Label
            {
                Text = "Đang tải câu hỏi...",
                Font = new Font("Segoe UI", _currentFontSize, FontStyle.Regular),
                ForeColor = Color.FromArgb(30, 41, 59),
                Dock = DockStyle.Top,
                Height = 85
            };

            pnlOptions = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            rbA = new RadioButton { Text = "A. ", Dock = DockStyle.Top, Height = 45, Font = new Font("Segoe UI", 10.5F) };
            rbB = new RadioButton { Text = "B. ", Dock = DockStyle.Top, Height = 45, Font = new Font("Segoe UI", 10.5F) };
            rbC = new RadioButton { Text = "C. ", Dock = DockStyle.Top, Height = 45, Font = new Font("Segoe UI", 10.5F) };
            rbD = new RadioButton { Text = "D. ", Dock = DockStyle.Top, Height = 45, Font = new Font("Segoe UI", 10.5F) };

            rbA.CheckedChanged += (s, e) => { if (rbA.Checked) SaveAnswer("A"); };
            rbB.CheckedChanged += (s, e) => { if (rbB.Checked) SaveAnswer("B"); };
            rbC.CheckedChanged += (s, e) => { if (rbC.Checked) SaveAnswer("C"); };
            rbD.CheckedChanged += (s, e) => { if (rbD.Checked) SaveAnswer("D"); };

            txtEssay = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", _currentFontSize),
                ScrollBars = ScrollBars.Vertical,
                ShortcutsEnabled = false, // Disable right click paste
                Visible = false
            };
            txtEssay.TextChanged += (s, e) => SaveEssayAnswer(txtEssay.Text);

            pnlOptions.Controls.Add(txtEssay);
            pnlOptions.Controls.Add(rbD);
            pnlOptions.Controls.Add(rbC);
            pnlOptions.Controls.Add(rbB);
            pnlOptions.Controls.Add(rbA);

            var bottomNav = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50
            };

            btnPrev = new Button
            {
                Text = "← Câu trước",
                Location = new Point(0, 8),
                Size = new Size(115, 38),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat
            };
            btnPrev.Click += (s, e) => { if (_currentIndex > 0) LoadQuestion(_currentIndex - 1); };

            btnNext = new Button
            {
                Text = "Câu tiếp →",
                Location = new Point(125, 8),
                Size = new Size(115, 38),
                BackColor = Color.FromArgb(224, 231, 255),
                ForeColor = Color.FromArgb(67, 56, 202),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            btnNext.Click += (s, e) => { if (_currentIndex < _examData.Questions.Count - 1) LoadQuestion(_currentIndex + 1); };

            btnFlag = new Button
            {
                Text = "⚑ Đánh dấu xem lại",
                Location = new Point(250, 8),
                Size = new Size(160, 38),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(71, 85, 105),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F)
            };
            btnFlag.Click += BtnFlag_Click;

            bottomNav.Controls.Add(btnPrev);
            bottomNav.Controls.Add(btnNext);
            bottomNav.Controls.Add(btnFlag);

            card.Controls.Add(pnlOptions);
            card.Controls.Add(bottomNav);
            card.Controls.Add(lblQuestionBody);
            card.Controls.Add(lblQuestionHeader);

            centerPnl.Controls.Add(card);

            this.Controls.Add(centerPnl);
            this.Controls.Add(rightPnl);
            this.Controls.Add(leftPnl);
            this.Controls.Add(topBar);

            RenderPalette();
        }

        private void AdjustFontSize(float delta)
        {
            _currentFontSize = Math.Max(9.5f, Math.Min(15.0f, _currentFontSize + delta));
            lblQuestionBody.Font = new Font("Segoe UI", _currentFontSize, FontStyle.Regular);
            txtEssay.Font = new Font("Segoe UI", _currentFontSize, FontStyle.Regular);
        }

        private void BtnFlag_Click(object? sender, EventArgs e)
        {
            if (_currentIndex < 0 || _currentIndex >= _examData.Questions.Count) return;
            int qId = _examData.Questions[_currentIndex].Id;

            if (_flaggedQuestions.Contains(qId))
            {
                _flaggedQuestions.Remove(qId);
            }
            else
            {
                _flaggedQuestions.Add(qId);
            }

            UpdateFlagButtonState(qId);
            UpdatePaletteColors();
            SaveDraftToDisk();
        }

        private void UpdateFlagButtonState(int qId)
        {
            if (_flaggedQuestions.Contains(qId))
            {
                btnFlag.Text = "★ Đã đánh dấu xem lại";
                btnFlag.BackColor = Color.FromArgb(254, 240, 138); // Yellow
                btnFlag.ForeColor = Color.FromArgb(133, 77, 14);
            }
            else
            {
                btnFlag.Text = "⚑ Đánh dấu xem lại";
                btnFlag.BackColor = Color.FromArgb(241, 245, 249);
                btnFlag.ForeColor = Color.FromArgb(71, 85, 105);
            }
        }

        private void RenderPalette()
        {
            pnlPalette.Controls.Clear();
            for (int i = 0; i < _examData.Questions.Count; i++)
            {
                int qIndex = i;
                var btn = new Button
                {
                    Text = (i + 1).ToString(),
                    Width = 40,
                    Height = 40,
                    Tag = i,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(241, 245, 249),
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold)
                };
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
                btn.Click += (s, e) => LoadQuestion(qIndex);
                pnlPalette.Controls.Add(btn);
            }
            UpdatePaletteColors();
        }

        private void UpdatePaletteColors()
        {
            foreach (Control c in pnlPalette.Controls)
            {
                if (c is Button b && b.Tag is int idx && idx < _examData.Questions.Count)
                {
                    var qId = _examData.Questions[idx].Id;
                    bool isAnswered = _mcqAnswers.ContainsKey(qId) || (_essayAnswers.ContainsKey(qId) && !string.IsNullOrWhiteSpace(_essayAnswers[qId]));
                    bool isFlagged = _flaggedQuestions.Contains(qId);

                    if (idx == _currentIndex)
                    {
                        b.BackColor = Color.FromArgb(79, 70, 229); // Purple selected
                        b.ForeColor = Color.White;
                    }
                    else if (isFlagged)
                    {
                        b.BackColor = Color.FromArgb(254, 240, 138); // Yellow flagged
                        b.ForeColor = Color.FromArgb(133, 77, 14);
                    }
                    else if (isAnswered)
                    {
                        b.BackColor = Color.FromArgb(209, 250, 229); // Green answered
                        b.ForeColor = Color.FromArgb(6, 95, 70);
                    }
                    else
                    {
                        b.BackColor = Color.FromArgb(241, 245, 249); // Gray unanswered
                        b.ForeColor = Color.FromArgb(51, 65, 85);
                    }
                }
            }
        }

        private void LoadQuestion(int index)
        {
            if (index < 0 || index >= _examData.Questions.Count) return;
            _currentIndex = index;
            var q = _examData.Questions[index];

            string cloInfo = !string.IsNullOrEmpty(q.CloCode) ? $" • {q.CloCode}" : "";
            lblQuestionHeader.Text = $"CÂU {index + 1} / {_examData.Questions.Count} • [{(q.Type == "mcq" ? "TRẮC NGHIỆM" : "TỰ LUẬN / VẤN ĐÁP")}]{cloInfo} • Độ khó: {q.Difficulty}";
            lblQuestionBody.Text = q.QuestionText;

            if (q.Type == "mcq")
            {
                txtEssay.Visible = false;
                rbA.Visible = rbB.Visible = rbC.Visible = rbD.Visible = true;

                rbA.Text = $"A. {q.OptionA}";
                rbB.Text = $"B. {q.OptionB}";
                rbC.Text = $"C. {q.OptionC}";
                rbD.Text = $"D. {q.OptionD}";

                _mcqAnswers.TryGetValue(q.Id, out var saved);
                rbA.Checked = saved == "A";
                rbB.Checked = saved == "B";
                rbC.Checked = saved == "C";
                rbD.Checked = saved == "D";
            }
            else
            {
                rbA.Visible = rbB.Visible = rbC.Visible = rbD.Visible = false;
                txtEssay.Visible = true;
                _essayAnswers.TryGetValue(q.Id, out var savedEssay);
                txtEssay.Text = savedEssay ?? "";
            }

            UpdateFlagButtonState(q.Id);
            UpdatePaletteColors();
        }

        private void SaveAnswer(string option)
        {
            if (_currentIndex < 0 || _currentIndex >= _examData.Questions.Count) return;
            var q = _examData.Questions[_currentIndex];
            _mcqAnswers[q.Id] = option;
            UpdatePaletteColors();
            SaveDraftToDisk();
        }

        private void SaveEssayAnswer(string essayText)
        {
            if (_currentIndex < 0 || _currentIndex >= _examData.Questions.Count) return;
            var q = _examData.Questions[_currentIndex];
            _essayAnswers[q.Id] = essayText;
            UpdatePaletteColors();
            SaveDraftToDisk();
        }

        private string GetDraftPath() => Path.Combine(Path.GetTempPath(), $"ictu_exam_{_examData.SessionId}_draft.json");

        private void SaveDraftToDisk()
        {
            try
            {
                var draft = new
                {
                    SessionId = _examData.SessionId,
                    SavedAt = DateTime.UtcNow,
                    McqAnswers = _mcqAnswers,
                    EssayAnswers = _essayAnswers,
                    Flagged = _flaggedQuestions.ToList()
                };
                string json = JsonSerializer.Serialize(draft);
                File.WriteAllText(GetDraftPath(), json);
            }
            catch { }
        }

        private void RestoreDraftFromDisk()
        {
            try
            {
                string path = GetDraftPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("McqAnswers", out var mcqElem))
                    {
                        foreach (var prop in mcqElem.EnumerateObject())
                        {
                            if (int.TryParse(prop.Name, out int qId))
                                _mcqAnswers[qId] = prop.Value.GetString() ?? "";
                        }
                    }
                    if (doc.RootElement.TryGetProperty("EssayAnswers", out var essayElem))
                    {
                        foreach (var prop in essayElem.EnumerateObject())
                        {
                            if (int.TryParse(prop.Name, out int qId))
                                _essayAnswers[qId] = prop.Value.GetString() ?? "";
                        }
                    }
                    if (doc.RootElement.TryGetProperty("Flagged", out var flaggedElem))
                    {
                        foreach (var item in flaggedElem.EnumerateArray())
                        {
                            _flaggedQuestions.Add(item.GetInt32());
                        }
                    }
                }
            }
            catch { }
        }

        private void StartCountdown()
        {
            _examTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _examTimer.Tick += (s, e) =>
            {
                _timeLeftSeconds--;
                int m = _timeLeftSeconds / 60;
                int sec = _timeLeftSeconds % 60;
                lblTimer.Text = $"{m:D2}:{sec:D2}";

                if (_timeLeftSeconds <= 0)
                {
                    _examTimer.Stop();
                    _isInternalDialogOpen = true;
                    try
                    {
                        MessageBox.Show("Hết giờ làm bài! Hệ thống tự động nộp bài thi.", "Hết giờ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    finally
                    {
                        _isInternalDialogOpen = false;
                    }
                    SubmitExam();
                }
            };
            _examTimer.Start();
        }

        private async void BtnAiSend_Click(object? sender, EventArgs e)
        {
            var msg = txtAiInput.Text.Trim();
            if (string.IsNullOrEmpty(msg)) return;
            txtAiInput.Text = "";

            rtbAiChat.AppendText($"[Bạn]: {msg}\n");
            rtbAiChat.ScrollToCaret();

            btnAiSend.Enabled = false;
            var currentQ = _examData.Questions.Count > _currentIndex ? _examData.Questions[_currentIndex].QuestionText : "";
            int examId = _examData.ExamId > 0 ? _examData.ExamId : 1;

            var reply = await _api.SendOralChatAsync(_studentCode, examId, msg, currentQ);
            rtbAiChat.AppendText($"[AI Giám Khảo]: {reply}\n\n");
            rtbAiChat.ScrollToCaret();
            btnAiSend.Enabled = true;
        }

        private void BtnSubmit_Click(object? sender, EventArgs e)
        {
            _isInternalDialogOpen = true;
            try
            {
                int answeredCount = _mcqAnswers.Count + _essayAnswers.Count(kv => !string.IsNullOrWhiteSpace(kv.Value));
                int totalCount = _examData.Questions.Count;

                string confirmMsg = $"Bạn đã làm {answeredCount}/{totalCount} câu.\nBạn có chắc chắn muốn nộp bài thi ngay bây giờ?";
                if (MessageBox.Show(confirmMsg, "Xác nhận nộp bài", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    SubmitExam();
                }
            }
            finally
            {
                _isInternalDialogOpen = false;
            }
        }

        private async void SubmitExam()
        {
            _isSubmitted = true;
            CleanupTimersAndHook();
            btnSubmit.Enabled = false;

            var answers = new List<AnswerDto>();
            foreach (var q in _examData.Questions)
            {
                _mcqAnswers.TryGetValue(q.Id, out var mcq);
                _essayAnswers.TryGetValue(q.Id, out var essay);
                answers.Add(new AnswerDto
                {
                    QuestionId = q.Id,
                    SelectedOption = mcq,
                    EssayAnswer = essay
                });
            }

            var subRes = await _api.SubmitExamAsync(_examData.SessionId, answers);
            if (subRes != null && subRes.Success)
            {
                // Delete local draft on successful submission
                try { File.Delete(GetDraftPath()); } catch { }

                this.Hide();
                var resultForm = new ResultForm(subRes, _violationCount);
                resultForm.ShowDialog();
                this.Close();
            }
            else
            {
                _isSubmitted = false;
                _isInternalDialogOpen = true;
                try
                {
                    MessageBox.Show("Lỗi kết nối khi nộp bài. Vui lòng thử lại.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _isInternalDialogOpen = false;
                }
                btnSubmit.Enabled = true;
            }
        }
    }
}
