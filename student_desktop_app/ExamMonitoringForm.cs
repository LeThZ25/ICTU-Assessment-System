using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace student_desktop_app
{
    /// <summary>
    /// Chế độ Monitoring (Giám sát kiểm tra trên lớp):
    /// - Chỉ Giảng viên (GV) mới có quyền thiết lập / chuyển đổi chế độ này trước khi mở bài thi trên lớp.
    /// - Khi sinh viên làm bài trên lớp ở chế độ Monitoring, máy tính của Giảng viên đóng vai trò là
    ///   1 Server tạm thời (Temporary Code Submission Server) chỉ dùng để nhận và lưu các file code
    ///   mà sinh viên gửi lên.
    /// - Sau khi sinh viên nộp xong và không còn file code nào được nộp lên nữa, hệ thống tự động đóng Server tạm thời.
    /// </summary>
    public class ExamMonitoringForm : Form
    {
        private WebView2? _webView;
        private Panel _topMonitorBar = null!;
        private Label _lblModeStatus = null!;
        private Label _lblFocusStatus = null!;
        private Label _lblTempServerBadge = null!;
        private Label _lblViolationBadge = null!;
        private Button _btnUploadCodeFile = null!;

        private System.Windows.Forms.Timer? _monitorTimer;
        private System.Windows.Forms.Timer? _serverIdleWatchdogTimer;
        private readonly HashSet<string> _reportedProcesses = new();
        private readonly string[] _monitoredProcesses = new[]
        {
            "anydesk", "teamviewer", "ultraviewer", "discord", "obs64", "telegram", "zalo"
        };

        // Temporary Lecturer Code Server (chỉ mở để lưu file code của SV, tự đóng khi không còn file nộp lên)
        private HttpListener? _tempLecturerCodeServer;
        private CancellationTokenSource? _serverCts;
        private bool _isTempServerRunning = false;
        private string _lecturerServerEndpoint = "http://127.0.0.1:5055/";
        private string _lecturerCodeSaveDirectory = "";
        private int _activeUploadsInFlight = 0;
        private int _totalCodeFilesSaved = 0;
        private DateTime _lastCodeFileReceivedAt = DateTime.MinValue;
        private bool _noMoreFilesFlagged = false;

        private int _monitoringLogCount = 0;
        private int _activeSessionId = 0;
        private string _activeStudentCode;
        private string _activeStudentName;
        private bool _isExamActive = false;
        private bool _isInternalDialogOpen = false;
        private bool _isSwitchingToKiosk = false;
        private DateTime _lastFocusLostLog = DateTime.MinValue;

        private readonly ApiClient _api;
        private readonly HttpClient _codeUploadClient = new() { Timeout = TimeSpan.FromSeconds(15) };
        private readonly string _targetUrl;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        public ExamMonitoringForm(
            string serverUrl = "http://localhost:5000/student_web.html?mode=desktop_kiosk&examMode=monitoring",
            string studentCode = "DTC235210069",
            string studentName = "Phạm Hồng Ngọc Kiên",
            int activeSessionId = 0)
        {
            _targetUrl = EnsureMonitoringParam(serverUrl);
            _activeStudentCode = string.IsNullOrWhiteSpace(studentCode) ? "DTC235210069" : studentCode;
            _activeStudentName = string.IsNullOrWhiteSpace(studentName) ? "Phạm Hồng Ngọc Kiên" : studentName;
            _activeSessionId = activeSessionId;
            _api = new ApiClient("http://localhost:5000");

            _lecturerCodeSaveDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "LecturerTempServer_CodeSubmissions",
                DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(_lecturerCodeSaveDirectory);

            ConfigureMonitoringWindow();
            InitializeComponent();
            StartTemporaryLecturerCodeServer();
            SetupClassroomMonitoringTimer();
        }

        private static string EnsureMonitoringParam(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "http://localhost:5000/student_web.html?mode=desktop_kiosk&examMode=monitoring";

            if (url.Contains("examMode=kiosk", StringComparison.OrdinalIgnoreCase))
                return url.Replace("examMode=kiosk", "examMode=monitoring", StringComparison.OrdinalIgnoreCase);

            if (!url.Contains("examMode=", StringComparison.OrdinalIgnoreCase))
            {
                string sep = url.Contains("?") ? "&" : "?";
                return $"{url}{sep}examMode=monitoring";
            }
            return url;
        }

        private void ConfigureMonitoringWindow()
        {
            this.Text = "IAS - ICTU Assessment System [Chế độ Monitoring - Kiểm tra trên lớp]";
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.WindowState = FormWindowState.Maximized;
            this.MinimumSize = new Size(1080, 680);
            this.TopMost = false;
            this.KeyPreview = true;
            this.BackColor = Color.FromArgb(15, 23, 42);

            this.Deactivate += (s, e) =>
            {
                if (_isInternalDialogOpen || !_isExamActive) return;

                if ((DateTime.UtcNow - _lastFocusLostLog).TotalSeconds < 5) return;
                _lastFocusLostLog = DateTime.UtcNow;

                string activeWinTitle = GetActiveWindowTitle();
                UpdateFocusIndicator(false, activeWinTitle);
                RecordMonitoringEvent(
                    "MonitoringWindowSwitch",
                    $"Chế độ Monitoring trên lớp: Sinh viên chuyển sang cửa sổ khác ({activeWinTitle})");
            };

            this.Activated += (s, e) =>
            {
                UpdateFocusIndicator(true, "Cửa sổ bài làm IAS");
            };

            this.FormClosing += (s, e) =>
            {
                _monitorTimer?.Stop();
                _serverIdleWatchdogTimer?.Stop();
                StopTemporaryLecturerCodeServer("Đóng cửa sổ Monitoring");
            };
        }

        private void InitializeComponent()
        {
            _topMonitorBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(12, 6, 12, 6)
            };

            _lblModeStatus = new Label
            {
                Text = $"👁 MONITORING TRÊN LỚP | SV: {_activeStudentName} ({_activeStudentCode})",
                ForeColor = Color.FromArgb(56, 189, 248),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(12, 12)
            };

            _lblFocusStatus = new Label
            {
                Text = "● Đang tập trung làm bài",
                ForeColor = Color.FromArgb(52, 211, 153),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(375, 13)
            };

            _lblTempServerBadge = new Label
            {
                Text = "🖧 Server Code GV: Đang mở (0 file)",
                ForeColor = Color.FromArgb(167, 243, 208),
                BackColor = Color.FromArgb(6, 78, 59),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(7, 3, 7, 3),
                Location = new Point(560, 10)
            };

            _lblViolationBadge = new Label
            {
                Text = "Giám sát: 0 sự kiện",
                ForeColor = Color.FromArgb(251, 191, 36),
                BackColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(7, 3, 7, 3),
                Location = new Point(805, 10)
            };

            _btnUploadCodeFile = new Button
            {
                Text = "📤 Nộp File Code (Server GV)",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(5, 150, 105),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(190, 28),
                Location = new Point(this.ClientSize.Width - 204, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            _btnUploadCodeFile.FlatAppearance.BorderSize = 0;
            _btnUploadCodeFile.Click += async (s, e) => await PromptStudentUploadCodeFilesAsync();

            _topMonitorBar.Controls.Add(_lblModeStatus);
            _topMonitorBar.Controls.Add(_lblFocusStatus);
            _topMonitorBar.Controls.Add(_lblTempServerBadge);
            _topMonitorBar.Controls.Add(_lblViolationBadge);
            _topMonitorBar.Controls.Add(_btnUploadCodeFile);

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            this.Controls.Add(_webView);
            this.Controls.Add(_topMonitorBar);

            _ = InitializeWebViewAsync();
        }

        // =====================================================================
        // SERVER TẠM THỜI CỦA GIẢNG VIÊN CHỈ ĐỂ LƯU FILE CODE SINH VIÊN NỘP LÊN
        // Tự động đóng Server ngay khi không còn file code nào nộp lên nữa
        // =====================================================================
        private void StartTemporaryLecturerCodeServer()
        {
            if (_isTempServerRunning) return;

            try
            {
                Directory.CreateDirectory(_lecturerCodeSaveDirectory);
                _serverCts = new CancellationTokenSource();
                _tempLecturerCodeServer = new HttpListener();
                _tempLecturerCodeServer.Prefixes.Add(_lecturerServerEndpoint);
                _tempLecturerCodeServer.Start();
                _isTempServerRunning = true;
                _noMoreFilesFlagged = false;

                UpdateTempServerBadgeUI($"🖧 Server Code GV: Đang mở ({_totalCodeFilesSaved} file)", isRunning: true);

                // Luồng lắng nghe các file code sinh viên gửi lên máy Giảng viên
                _ = Task.Run(() => ListenForStudentCodeUploadsAsync(_serverCts.Token));

                // Bộ đếm kiểm tra trạng thái rỗi: khi đã đánh dấu hoàn tất nộp hoặc hết phiên mà không còn file nào đang tải lên -> đóng Server
                _serverIdleWatchdogTimer = new System.Windows.Forms.Timer { Interval = 3000 };
                _serverIdleWatchdogTimer.Tick += (s, e) =>
                {
                    if (!_isTempServerRunning) return;

                    // Nếu sinh viên/ca thi đã báo kết thúc nộp file code và không còn luồng upload nào đang chạy -> đóng Server ngay
                    if (_noMoreFilesFlagged && _activeUploadsInFlight <= 0)
                    {
                        StopTemporaryLecturerCodeServer("Không còn file code nào nộp lên nữa");
                        return;
                    }

                    // Nếu đã nhận file code và sau 10 phút không còn bất kỳ file code nào gửi thêm khi ca thi đã kết thúc -> tự đóng Server
                    if (!_isExamActive && _totalCodeFilesSaved > 0 && _activeUploadsInFlight <= 0 &&
                        _lastCodeFileReceivedAt != DateTime.MinValue &&
                        (DateTime.UtcNow - _lastCodeFileReceivedAt).TotalSeconds >= 15)
                    {
                        StopTemporaryLecturerCodeServer("Đã lưu xong toàn bộ file code, không còn file mới");
                    }
                };
                _serverIdleWatchdogTimer.Start();
            }
            catch
            {
                // Trường hợp cổng đã được một tiến trình Server GV khác mở sẵn trên máy
                _isTempServerRunning = true;
                UpdateTempServerBadgeUI("🖧 Server Code GV: Đã kết nối cổng 5055", isRunning: true);
            }
        }

        private async Task ListenForStudentCodeUploadsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _tempLecturerCodeServer != null && _tempLecturerCodeServer.IsListening)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = await _tempLecturerCodeServer.GetContextAsync();
                }
                catch
                {
                    break;
                }

                if (ctx == null) continue;

                _ = Task.Run(async () =>
                {
                    Interlocked.Increment(ref _activeUploadsInFlight);
                    try
                    {
                        var req = ctx.Request;
                        var res = ctx.Response;
                        res.Headers.Add("Access-Control-Allow-Origin", "*");
                        res.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                        if (req.HttpMethod == "OPTIONS")
                        {
                            res.StatusCode = 200;
                            res.Close();
                            return;
                        }

                        if (req.HttpMethod == "POST" && req.Url != null && req.Url.AbsolutePath.Contains("/upload-code", StringComparison.OrdinalIgnoreCase))
                        {
                            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                            string body = await reader.ReadToEndAsync();
                            using var doc = JsonDocument.Parse(body);
                            var root = doc.RootElement;

                            string studentCode = root.TryGetProperty("studentCode", out var scEl) ? (scEl.GetString() ?? "SV") : "SV";
                            string studentName = root.TryGetProperty("studentName", out var snEl) ? (snEl.GetString() ?? "") : "";
                            string fileName = root.TryGetProperty("fileName", out var fnEl) ? (fnEl.GetString() ?? "bai_lam.sql") : "bai_lam.sql";
                            string codeContent = root.TryGetProperty("codeContent", out var ccEl) ? (ccEl.GetString() ?? "") : "";
                            bool isFinalFile = root.TryGetProperty("isFinalSubmission", out var finEl) && finEl.GetBoolean();

                            // Làm sạch tên file để lưu an toàn trên máy Giảng viên
                            foreach (char c in Path.GetInvalidFileNameChars())
                            {
                                fileName = fileName.Replace(c, '_');
                            }
                            foreach (char c in Path.GetInvalidFileNameChars())
                            {
                                studentCode = studentCode.Replace(c, '_');
                            }

                            string studentFolder = Path.Combine(_lecturerCodeSaveDirectory, studentCode);
                            Directory.CreateDirectory(studentFolder);

                            string timestampedFileName = $"{DateTime.Now:HHmmss}_{fileName}";
                            string fullSavePath = Path.Combine(studentFolder, timestampedFileName);

                            // Chỉ lưu file code của sinh viên
                            string headerComment = $"// Mã SV: {studentCode} | Họ tên: {studentName} | Thời gian nộp: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n";
                            await File.WriteAllTextAsync(fullSavePath, headerComment + codeContent, Encoding.UTF8);

                            int savedCount = Interlocked.Increment(ref _totalCodeFilesSaved);
                            _lastCodeFileReceivedAt = DateTime.UtcNow;

                            if (isFinalFile)
                            {
                                _noMoreFilesFlagged = true;
                            }

                            UpdateTempServerBadgeUI($"🖧 Server Code GV: Đã lưu {savedCount} file", isRunning: true);

                            byte[] respBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                            {
                                success = true,
                                savedPath = fullSavePath,
                                totalSaved = savedCount,
                                serverClosing = isFinalFile
                            }));
                            res.ContentType = "application/json; charset=utf-8";
                            res.StatusCode = 200;
                            await res.OutputStream.WriteAsync(respBytes, 0, respBytes.Length);
                            res.Close();

                            // Nếu đây là file cuối cùng và không còn file nào đang gửi lên nữa -> đóng Server ngay
                            if (_noMoreFilesFlagged && Volatile.Read(ref _activeUploadsInFlight) <= 1)
                            {
                                await Task.Delay(300);
                                StopTemporaryLecturerCodeServer("Đã nhận hết file code của sinh viên — Tự động đóng Server");
                            }
                        }
                        else
                        {
                            res.StatusCode = 404;
                            res.Close();
                        }
                    }
                    catch
                    {
                        try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeUploadsInFlight);
                    }
                }, token);
            }
        }

        public void StopTemporaryLecturerCodeServer(string reason)
        {
            if (!_isTempServerRunning) return;
            _isTempServerRunning = false;

            try { _serverCts?.Cancel(); } catch { }
            try
            {
                if (_tempLecturerCodeServer != null)
                {
                    if (_tempLecturerCodeServer.IsListening)
                        _tempLecturerCodeServer.Stop();
                    _tempLecturerCodeServer.Close();
                    _tempLecturerCodeServer = null;
                }
            }
            catch { }

            UpdateTempServerBadgeUI($"📴 Server Code GV: Đã đóng (Đã lưu {_totalCodeFilesSaved} file)", isRunning: false);

            try
            {
                if (_webView != null && _webView.CoreWebView2 != null && this.IsHandleCreated)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            string msg = JsonSerializer.Serialize(new
                            {
                                eventType = "lecturerTempServerClosed",
                                totalFilesSaved = _totalCodeFilesSaved,
                                saveDirectory = _lecturerCodeSaveDirectory,
                                reason = reason
                            });
                            _webView.CoreWebView2.PostWebMessageAsJson(msg);
                        }
                        catch { }
                    }));
                }
            }
            catch { }
        }

        private void UpdateTempServerBadgeUI(string text, bool isRunning)
        {
            if (!this.IsHandleCreated) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateTempServerBadgeUI(text, isRunning)));
                return;
            }

            _lblTempServerBadge.Text = text;
            if (isRunning)
            {
                _lblTempServerBadge.ForeColor = Color.FromArgb(167, 243, 208);
                _lblTempServerBadge.BackColor = Color.FromArgb(6, 78, 59);
            }
            else
            {
                _lblTempServerBadge.ForeColor = Color.FromArgb(226, 232, 240);
                _lblTempServerBadge.BackColor = Color.FromArgb(51, 65, 85);
            }
        }

        private async Task<bool> SendCodeFileToLecturerServerAsync(string fileName, string codeContent, bool isFinalSubmission)
        {
            // Nếu Server tạm thời của GV đang đóng mà sinh viên nộp trong giờ thi, tự khởi động lại để nhận rồi đóng ngay khi hết file
            if (!_isTempServerRunning)
            {
                StartTemporaryLecturerCodeServer();
            }

            try
            {
                string uploadUrl = $"{_lecturerServerEndpoint.TrimEnd('/')}/upload-code";
                var payload = new
                {
                    sessionId = _activeSessionId,
                    studentCode = _activeStudentCode,
                    studentName = _activeStudentName,
                    fileName = fileName,
                    codeContent = codeContent,
                    isFinalSubmission = isFinalSubmission
                };

                var response = await _codeUploadClient.PostAsJsonAsync(uploadUrl, payload);
                if (response.IsSuccessStatusCode)
                {
                    RecordMonitoringEvent("CodeFileSubmitted", $"Đã gửi file code '{fileName}' lên Server tạm thời của Giảng viên");
                    return true;
                }
            }
            catch
            {
                // Fallback lưu trực tiếp vào thư mục Server tạm thời trên máy nếu kết nối mạng cục bộ bị tường lửa chặn
                try
                {
                    string studentFolder = Path.Combine(_lecturerCodeSaveDirectory, _activeStudentCode);
                    Directory.CreateDirectory(studentFolder);
                    string fullSavePath = Path.Combine(studentFolder, $"{DateTime.Now:HHmmss}_{fileName}");
                    await File.WriteAllTextAsync(fullSavePath, codeContent, Encoding.UTF8);
                    _totalCodeFilesSaved++;
                    _lastCodeFileReceivedAt = DateTime.UtcNow;
                    if (isFinalSubmission)
                    {
                        _noMoreFilesFlagged = true;
                        StopTemporaryLecturerCodeServer("Đã nhận hết file code của sinh viên — Tự động đóng Server");
                    }
                    return true;
                }
                catch { }
            }
            return false;
        }

        private async Task PromptStudentUploadCodeFilesAsync()
        {
            _isInternalDialogOpen = true;
            try
            {
                using var ofd = new OpenFileDialog
                {
                    Title = "Chọn các file code bài làm để gửi lên Server tạm thời của Giảng viên",
                    Filter = "Tệp mã nguồn (*.sql;*.cs;*.py;*.cpp;*.c;*.java;*.js;*.html;*.css;*.txt)|*.sql;*.cs;*.py;*.cpp;*.c;*.java;*.js;*.html;*.css;*.txt|Tất cả các tệp (*.*)|*.*",
                    Multiselect = true
                };

                if (ofd.ShowDialog(this) != DialogResult.OK || ofd.FileNames.Length == 0)
                    return;

                // Hỏi sinh viên xem đây đã phải lần nộp file code cuối cùng chưa để đóng Server tạm thời của GV
                var confirmFinal = MessageBox.Show(
                    this,
                    $"Bạn đã chọn {ofd.FileNames.Length} file code để gửi lên Server tạm thời của Giảng viên.\n\nSau khi gửi đợt này, bạn còn file code nào cần nộp thêm nữa không?\n• Chọn [Không (No)] nếu đây là file cuối cùng -> Server tạm thời của GV sẽ tự động đóng sau khi lưu xong.\n• Chọn [Có (Yes)] nếu bạn vẫn còn file code khác muốn nộp tiếp.",
                    "Xác nhận trạng thái nộp file code",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (confirmFinal == DialogResult.Cancel) return;
                bool isLastBatch = (confirmFinal == DialogResult.No);

                int successCount = 0;
                for (int i = 0; i < ofd.FileNames.Length; i++)
                {
                    string path = ofd.FileNames[i];
                    string name = Path.GetFileName(path);
                    string content = await File.ReadAllTextAsync(path, Encoding.UTF8);
                    bool isFinalFile = isLastBatch && (i == ofd.FileNames.Length - 1);

                    if (await SendCodeFileToLecturerServerAsync(name, content, isFinalFile))
                    {
                        successCount++;
                    }
                }

                string statusMsg = isLastBatch
                    ? $"Đã gửi thành công {successCount}/{ofd.FileNames.Length} file code lên máy Giảng viên.\n\nVì không còn file code nào nộp lên nữa, Server tạm thời của Giảng viên đã tự động đóng!\nThư mục lưu: {_lecturerCodeSaveDirectory}"
                    : $"Đã gửi thành công {successCount}/{ofd.FileNames.Length} file code lên Server tạm thời của Giảng viên.\nServer vẫn đang mở để chờ các file code tiếp theo của bạn.";

                MessageBox.Show(this, statusMsg, "Kết quả nộp file code (Monitoring)", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                _isInternalDialogOpen = false;
            }
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                string userDataFolder = Path.Combine(Path.GetTempPath(), "ICTU_Exam_WebView2_Profile");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                await _webView!.EnsureCoreWebView2Async(env);

                var settings = _webView.CoreWebView2.Settings;
                settings.AreDevToolsEnabled = false;
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = true;
                settings.IsBuiltInErrorPageEnabled = false;

                _webView.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                };

                _webView.CoreWebView2.WebMessageReceived += HandleWebMessageReceived;

                string navUrl = _targetUrl.Contains("?")
                    ? $"{_targetUrl}&_t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                    : $"{_targetUrl}?_t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                _webView.CoreWebView2.Navigate(navUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Lỗi khởi tạo chế độ Monitoring: {ex.Message}", "Lỗi hệ thống", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupClassroomMonitoringTimer()
        {
            _monitorTimer = new System.Windows.Forms.Timer { Interval = 8000 };
            _monitorTimer.Tick += (s, e) =>
            {
                if (!_isExamActive) return;

                try
                {
                    IntPtr fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero && fg != this.Handle)
                    {
                        string title = GetActiveWindowTitle();
                        UpdateFocusIndicator(false, title);
                    }
                    else
                    {
                        UpdateFocusIndicator(true, "Cửa sổ bài làm IAS");
                    }

                    var running = Process.GetProcesses();
                    foreach (var p in running)
                    {
                        string pName = p.ProcessName.ToLowerInvariant();
                        foreach (var watched in _monitoredProcesses)
                        {
                            if (pName.Contains(watched) && !_reportedProcesses.Contains(pName))
                            {
                                _reportedProcesses.Add(pName);
                                RecordMonitoringEvent(
                                    $"MonitoredProcess ({p.ProcessName})",
                                    $"Chế độ Monitoring phát hiện ứng dụng đang mở trong giờ kiểm tra trên lớp: {p.ProcessName}");
                            }
                        }
                    }
                }
                catch { }
            };
            _monitorTimer.Start();
        }

        private string GetActiveWindowTitle()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero) return "Không xác định";
                var sb = new StringBuilder(256);
                if (GetWindowText(handle, sb, 256) > 0)
                {
                    string t = sb.ToString().Trim();
                    return string.IsNullOrEmpty(t) ? "Ứng dụng ngoài" : t;
                }
            }
            catch { }
            return "Ứng dụng ngoài";
        }

        private void UpdateFocusIndicator(bool isFocused, string windowName)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateFocusIndicator(isFocused, windowName)));
                return;
            }

            if (isFocused)
            {
                _lblFocusStatus.Text = "● Đang tập trung làm bài";
                _lblFocusStatus.ForeColor = Color.FromArgb(52, 211, 153);
            }
            else
            {
                string shortTitle = windowName.Length > 24 ? windowName.Substring(0, 24) + "..." : windowName;
                _lblFocusStatus.Text = $"⚠ Rời cửa sổ ({shortTitle})";
                _lblFocusStatus.ForeColor = Color.FromArgb(251, 113, 133);
            }
        }

        public async void RecordMonitoringEvent(string type, string details)
        {
            _monitoringLogCount++;
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(new Action(() =>
                {
                    _lblViolationBadge.Text = $"Giám sát: {_monitoringLogCount} sự kiện";
                }));
            }

            try
            {
                if (_webView != null && _webView.CoreWebView2 != null)
                {
                    string jsonMsg = JsonSerializer.Serialize(new
                    {
                        eventType = "nativeViolation",
                        violationCount = _monitoringLogCount,
                        type = type,
                        details = details,
                        mode = "monitoring"
                    });
                    _webView.CoreWebView2.PostWebMessageAsJson(jsonMsg);
                }
            }
            catch { }

            await _api.LogViolationAsync(_activeSessionId, _activeStudentCode, _activeStudentName, type, details);
        }

        private async void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(raw)) return;

                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("action", out var actionElem)) return;

                string action = actionElem.GetString() ?? "";
                if (action == "exit")
                {
                    _monitorTimer?.Stop();
                    StopTemporaryLecturerCodeServer("Thoát ứng dụng");
                    this.Close();
                }
                else if (action == "examStarted")
                {
                    if (doc.RootElement.TryGetProperty("sessionId", out var sidElem) && sidElem.TryGetInt32(out int sid))
                        _activeSessionId = sid;
                    if (doc.RootElement.TryGetProperty("studentCode", out var scElem))
                        _activeStudentCode = scElem.GetString() ?? _activeStudentCode;
                    if (doc.RootElement.TryGetProperty("studentName", out var snElem))
                        _activeStudentName = snElem.GetString() ?? _activeStudentName;

                    string examCategory = doc.RootElement.TryGetProperty("examCategory", out var catEl) ? (catEl.GetString() ?? "") : "";
                    string examMode = doc.RootElement.TryGetProperty("examMode", out var modeEl) ? (modeEl.GetString() ?? "monitoring") : "monitoring";

                    if (string.Equals(examCategory, "regular", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(examCategory, "homework", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(examMode, "kiosk", StringComparison.OrdinalIgnoreCase))
                    {
                        SwitchToKioskForm(forceForHomework: string.Equals(examCategory, "regular", StringComparison.OrdinalIgnoreCase));
                        return;
                    }

                    // Khi bắt đầu làm bài trên lớp ở chế độ Monitoring -> mở Server tạm thời của GV để nhận file code
                    StartTemporaryLecturerCodeServer();

                    _isExamActive = true;
                    _monitoringLogCount = 0;
                    _lblModeStatus.Text = $"👁 MONITORING TRÊN LỚP (ĐANG THI) | SV: {_activeStudentName} ({_activeStudentCode})";
                    _lblViolationBadge.Text = "Giám sát: 0 sự kiện";
                }
                else if (action == "submitCodeToLecturerServer")
                {
                    // Nhận file code từ giao diện bài thi gửi lên Server tạm thời của Giảng viên
                    string fileName = doc.RootElement.TryGetProperty("fileName", out var fnEl) ? (fnEl.GetString() ?? "code_submission.sql") : "code_submission.sql";
                    string codeContent = doc.RootElement.TryGetProperty("codeContent", out var ccEl) ? (ccEl.GetString() ?? "") : "";
                    bool isFinal = doc.RootElement.TryGetProperty("isFinalSubmission", out var finEl) && finEl.GetBoolean();

                    if (!string.IsNullOrWhiteSpace(codeContent))
                    {
                        await SendCodeFileToLecturerServerAsync(fileName, codeContent, isFinal);
                    }
                    else if (isFinal)
                    {
                        _noMoreFilesFlagged = true;
                        if (Volatile.Read(ref _activeUploadsInFlight) <= 0)
                        {
                            StopTemporaryLecturerCodeServer("Không còn file code nào nộp lên nữa");
                        }
                    }
                }
                else if (action == "openNativeCodeFilePicker")
                {
                    await PromptStudentUploadCodeFilesAsync();
                }
                else if (action == "examEnded")
                {
                    _activeSessionId = 0;
                    _isExamActive = false;
                    _noMoreFilesFlagged = true;
                    _lblModeStatus.Text = $"👁 MONITORING TRÊN LỚP | SV: {_activeStudentName} ({_activeStudentCode})";

                    // Khi bài thi kết thúc và không còn file code nào đang nộp lên nữa -> tự động đóng Server tạm thời của Giảng viên
                    if (Volatile.Read(ref _activeUploadsInFlight) <= 0)
                    {
                        StopTemporaryLecturerCodeServer("Sinh viên đã hoàn tất nộp bài, không còn file code nào nộp lên nữa");
                    }
                }
                else if (action == "switchExamExecutionMode")
                {
                    string targetMode = doc.RootElement.TryGetProperty("targetMode", out var tmEl) ? (tmEl.GetString() ?? "") : "";
                    bool isHomework = doc.RootElement.TryGetProperty("isHomework", out var hwEl) && hwEl.GetBoolean();
                    if (string.Equals(targetMode, "kiosk", StringComparison.OrdinalIgnoreCase) || isHomework)
                    {
                        SwitchToKioskForm(forceForHomework: isHomework);
                    }
                }
            }
            catch { }
        }

        public void SwitchToKioskForm(bool forceForHomework)
        {
            if (_isSwitchingToKiosk) return;
            _isSwitchingToKiosk = true;

            _monitorTimer?.Stop();
            _serverIdleWatchdogTimer?.Stop();
            StopTemporaryLecturerCodeServer("Chuyển sang chế độ Kiosk");

            string kioskUrl = _targetUrl.Replace("examMode=monitoring", "examMode=kiosk", StringComparison.OrdinalIgnoreCase);
            if (!kioskUrl.Contains("examMode=", StringComparison.OrdinalIgnoreCase))
            {
                string sep = kioskUrl.Contains("?") ? "&" : "?";
                kioskUrl = $"{kioskUrl}{sep}examMode=kiosk";
            }

            if (forceForHomework)
            {
                MessageBox.Show(
                    this,
                    "Đối với Bài tập về nhà / Kiểm tra thường xuyên ở nhà, hệ thống bắt buộc sử dụng Chế độ Kiosk để sinh viên tự nhập tay đáp án và vấn đáp với AI.",
                    "Bắt buộc Chế độ Kiosk cho Bài tập về nhà",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            this.Hide();
            var kioskForm = new ExamKioskForm(kioskUrl, _activeStudentCode, _activeStudentName);
            kioskForm.FormClosed += (s, args) => this.Close();
            kioskForm.Show();
        }
    }
}
