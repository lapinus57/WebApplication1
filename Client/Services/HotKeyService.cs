using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Client.Helpers;
using Client.Models;

namespace Client.Services
{
    public sealed class HotKeyService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int VK_E = 0x45;
        private const int VK_V = 0x56;

        private const int VK_F5 = 0x74;
        private const int VK_F6 = 0x75;
        private const int VK_F7 = 0x76;
        private const int VK_F8 = 0x77;
        private const int VK_F9 = 0x78;
        private const int VK_F10 = 0x79;
        private const int VK_F11 = 0x7A;
        private const int VK_F12 = 0x7B;

        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;

        private const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private static bool IsNotepadActive()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName.Equals("notepad", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
        private static string GetActiveWindowTitle()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return string.Empty;
            var sb = new System.Text.StringBuilder(256);
            if (GetWindowText(hwnd, sb, sb.Capacity) > 0)
                return sb.ToString().Trim();
            return string.Empty;
        }

        private static string ProcessTemplate(string text)
        {
            return text.Replace("[ROOM]", Environment.UserName)
                       .Replace("[NEWLINE]", System.Environment.NewLine)
                       .Replace("[TIME]", DateTime.Now.ToString("HH:mm"));
        }


        private static void PasteClipboardAndSendF12()
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_F12, 0, 0, UIntPtr.Zero);
            keybd_event(VK_F12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        private static void BringMainWindowToForeground()
        {
            if (App.MainWindow != null)
            {
                var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                SetForegroundWindow(hwnd);
                App.MainWindow.Activate();
            }
        }

        private static bool TryExtractPatientFromTitle(string title, out string patientTitle, out string lastName, out string firstName)
        {
            patientTitle = string.Empty;
            lastName = string.Empty;
            firstName = string.Empty;
            if (string.IsNullOrWhiteSpace(title))
                return false;

            string[] patterns = new[] { "ORTHOPTIE de ", "PATHOLOGIES de ", "LENTILLES de ", "REFRACTION de " };
            foreach (var p in patterns)
            {
                int idx = title.IndexOf(p, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string remaining = title[(idx + p.Length)..];
                    int commaIndex = remaining.IndexOf(',');
                    if (commaIndex > 0)
                        remaining = remaining.Substring(0, commaIndex);
                    PatientStringHelper.ExtractInfoFromInput(remaining.Trim(), out patientTitle, out lastName, out firstName);
                    return !string.IsNullOrWhiteSpace(lastName) || !string.IsNullOrWhiteSpace(firstName);
                }
            }
            return false;
        }

        private static string? FindExamWindowTitle()
        {
            string? foundTitle = null;
            EnumWindows((hWnd, lParam) =>
            {
                var sb = new System.Text.StringBuilder(256);
                if (GetWindowText(hWnd, sb, sb.Capacity) > 0)
                {
                    var t = sb.ToString();
                    if (TryExtractPatientFromTitle(t, out _, out _, out _))
                    {
                        foundTitle = t;
                        return false; // stop enumeration
                    }
                }
                return true;
            }, IntPtr.Zero);
            return foundTitle;
        }
        public static async Task ShowPatientDialogAsync(string examName, string? patientName = null)
        {
            var options = ExamOption.Load();
            var rooms = RoomList.Load();

            var dialog = new ContentDialog
            {
                Title = "Ajout d'un patient",
                PrimaryButtonText = "Valider",
                CloseButtonText = "Annuler",
                XamlRoot = App.MainWindow.Content.XamlRoot,
            };

            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 5; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var nameBox = new TextBox { PlaceholderText = "Nom du patient" ,Width = 600};
            var examCombo = new ComboBox
            {
                ItemsSource = options,
                DisplayMemberPath = "Name",
                SelectedValuePath = "Name",
                SelectedValue = examName,
                Width = 600
            };
            var eyeCombo = new ComboBox { Width = 600 };
            eyeCombo.Items.Add(new ComboBoxItem { Content = "ODG" });
            eyeCombo.Items.Add(new ComboBoxItem { Content = "OD" });
            eyeCombo.Items.Add(new ComboBoxItem { Content = "OG" });
            eyeCombo.SelectedIndex = 0;

            var floorCombo = new ComboBox { Width = 600, ItemsSource = rooms };
            var examOpt = options.FirstOrDefault(o => o.Name == examName);
            if (examOpt != null && rooms.Contains(examOpt.Floor))
                floorCombo.SelectedItem = examOpt.Floor;
            var commentBox = new TextBox { PlaceholderText = "Commentaire", Width = 600 };

            // Extract patient name either from parameter or active window title
            if (string.IsNullOrWhiteSpace(patientName))
            {
                var title = GetActiveWindowTitle();
                Debug.WriteLine($"[HotKeyService] Active window title: {title}");

                if (!TryExtractPatientFromTitle(title, out var patientTitle, out var lastName, out var firstName))
                {
                    var other = FindExamWindowTitle();
                    if (!string.IsNullOrEmpty(other))
                    {
                        Debug.WriteLine($"[HotKeyService] Using window title: {other}");
                        TryExtractPatientFromTitle(other, out patientTitle, out lastName, out firstName);
                    }
                }

                if (!string.IsNullOrWhiteSpace(patientTitle) || !string.IsNullOrWhiteSpace(lastName) || !string.IsNullOrWhiteSpace(firstName))
                    nameBox.Text = $"{patientTitle} {lastName} {firstName}".Trim();
            }
            else
            {
                nameBox.Text = patientName;
            }

            // Layout
            AddLabeledControl(grid, 0, "Nom", nameBox);
            AddLabeledControl(grid, 1, "Examen", examCombo);
            AddLabeledControl(grid, 2, "Œil", eyeCombo);
            AddLabeledControl(grid, 3, "Salle", floorCombo);
            AddLabeledControl(grid, 4, "Commentaire", commentBox);

            dialog.Content = grid;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var inputName = nameBox.Text.Trim();
                PatientStringHelper.ExtractInfoFromInput(inputName,
                    out var titleBox, out var lastNameBox, out var firstNameBox);

                var selectedExam = examCombo.SelectedValue as string ?? string.Empty;
                var opt = options.FirstOrDefault(o => o.Name == selectedExam);

                var patient = new Patient
                {
                    Id = Guid.NewGuid().ToString(),
                    Colors = opt?.Color ?? string.Empty,
                    Title = titleBox,
                    LastName = lastNameBox,
                    FirstName = firstNameBox,
                    Exams = selectedExam,
                    Eye = (eyeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? string.Empty,
                    Annotation = string.IsNullOrWhiteSpace(commentBox.Text) ? opt?.Annotation ?? string.Empty : commentBox.Text.Trim(),
                    Position = floorCombo.SelectedItem as string ?? string.Empty,
                    HoldTime = DateTime.Now,
                    Examinator = App.UserName,
                   
                };

                try
                {
                    await App.ChatService.DeclarePatient(patient);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HotKeyService] Error declaring patient: {ex.Message}");
                }
            }
        }

        public static async Task ShowEditPatientDialogAsync(Patient patient)
        {
            var options = ExamOption.Load();
            var rooms = RoomList.Load();

            var dialog = new ContentDialog
            {
                Title = "Modifier un patient",
                PrimaryButtonText = "Valider",
                CloseButtonText = "Annuler",
                XamlRoot = App.MainWindow.Content.XamlRoot,
            };

            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 5; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var nameBox = new TextBox { Width = 600, Text = $"{patient.Title} {patient.LastName} {patient.FirstName}".Trim() };
            var examCombo = new ComboBox
            {
                ItemsSource = options,
                DisplayMemberPath = "Name",
                SelectedValuePath = "Name",
                SelectedValue = patient.Exams,
                Width = 600
            };
            var eyeCombo = new ComboBox { Width = 600 };
            eyeCombo.Items.Add(new ComboBoxItem { Content = "ODG" });
            eyeCombo.Items.Add(new ComboBoxItem { Content = "OD" });
            eyeCombo.Items.Add(new ComboBoxItem { Content = "OG" });
            eyeCombo.SelectedItem = eyeCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Content == patient.Eye) ?? eyeCombo.Items[0];

            var floorCombo = new ComboBox { Width = 600, ItemsSource = rooms, SelectedItem = patient.Position };
            var timePicker = new TimePicker { Width = 600, Time = patient.HoldTime.TimeOfDay };
            var commentBox = new TextBox { Width = 600, Text = patient.Annotation };

            AddLabeledControl(grid, 0, "Nom", nameBox);
            AddLabeledControl(grid, 1, "Examen", examCombo);
            AddLabeledControl(grid, 2, "Heure", timePicker);
            AddLabeledControl(grid, 3, "Salle", floorCombo);
            AddLabeledControl(grid, 4, "Commentaire", commentBox);

            dialog.Content = grid;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var inputName = nameBox.Text.Trim();
                PatientStringHelper.ExtractInfoFromInput(inputName,
                    out var titleBox, out var lastNameBox, out var firstNameBox);

                var selectedExam = examCombo.SelectedValue as string ?? string.Empty;
                var opt = options.FirstOrDefault(o => o.Name == selectedExam);

                patient.Title = titleBox;
                patient.LastName = lastNameBox;
                patient.FirstName = firstNameBox;
                patient.Exams = selectedExam;
                patient.Eye = (eyeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? string.Empty;
                patient.Annotation = string.IsNullOrWhiteSpace(commentBox.Text) ? opt?.Annotation ?? string.Empty : commentBox.Text.Trim();
                patient.Position = floorCombo.SelectedItem as string ?? string.Empty;
                patient.Colors = opt?.Color ?? string.Empty;
                var date = patient.HoldTime.Date;
                patient.HoldTime = date + timePicker.Time;

                try
                {
                    await App.ChatService.UpdatePatientAsync(patient);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HotKeyService] Error updating patient: {ex.Message}");
                }
            }
        }

        public static async Task DeclarePatientAsync(string examName, string patientName)
        {
            var options = ExamOption.Load();
            var opt = options.FirstOrDefault(o => o.Name.Equals(examName, StringComparison.OrdinalIgnoreCase));

            PatientStringHelper.ExtractInfoFromInput(patientName.Trim(),
                out var title, out var lastName, out var firstName);

            var patient = new Patient
            {
                Id = Guid.NewGuid().ToString(),
                Colors = opt?.Color ?? string.Empty,
                Title = title,
                LastName = lastName,
                FirstName = firstName,
                Exams = examName,
                Eye = "ODG",
                Annotation = opt?.Annotation ?? string.Empty,
                Position = opt?.Floor ?? string.Empty,
                HoldTime = DateTime.Now,
                Examinator = App.UserName,
            };

            try
            {
                await App.ChatService.DeclarePatient(patient);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HotKeyService] Error declaring patient: {ex.Message}");
            }
        }

        private static void AddLabeledControl(Grid grid, int row, string label, UIElement control)
        {
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, 0);
            grid.Children.Add(tb);

            Grid.SetRow((FrameworkElement)control, row);
            Grid.SetColumn((FrameworkElement)control, 1);
            grid.Children.Add(control);
        }


        public void Start()
        {
            if (_hookID != IntPtr.Zero) return;
            _proc = HookCallback;
            IntPtr module = GetModuleHandle(null);
            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, module, 0);
        }

        public void Dispose()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                if (lParam == IntPtr.Zero)
                    return CallNextHookEx(_hookID, nCode, wParam, lParam);

                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)kb.vkCode;
                if (vkCode == VK_E && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
                {
                    // Ctrl+E => show chat
                    App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                    {
                        App.MainWindow.Activate();
                        if (App.MainWindow is MainWindow mw)
                        {
                            var chat = mw.ShowChatPage();
                            chat.FocusInputBox();
                        }
                    });
                }
                else if (vkCode == VK_F5 || vkCode == VK_F6 || vkCode == VK_F7 || vkCode == VK_F8)
                {

                    var title = GetActiveWindowTitle();
                    string suffix = string.Empty;
                    if (title.StartsWith("REFRACTION", StringComparison.OrdinalIgnoreCase))
                        suffix = "Refraction";
                    else if (title.Contains("PATHOLOGIES", StringComparison.OrdinalIgnoreCase))
                        suffix = "Pathologies";
                    else if (title.Contains("LENTILLES", StringComparison.OrdinalIgnoreCase))
                        suffix = "Lentilles";
                    else if (title.Contains("ORTHOPTIE", StringComparison.OrdinalIgnoreCase))
                        suffix = "Orthoptie";

                    if (!string.IsNullOrEmpty(suffix))
                    {
                        string keyPrefix = vkCode switch
                        {
                            VK_F5 => "ShortcutF5",
                            VK_F6 => "ShortcutF6",
                            VK_F7 => "ShortcutF7",
                            VK_F8 => "ShortcutF7",
                            _ => string.Empty
                        };

                        var text = AppSettings.Get(keyPrefix + suffix, string.Empty);
                        if (!string.IsNullOrEmpty(text))
                        {
                            text = ProcessTemplate(text);
                            App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                            {
                                var data = new DataPackage();
                                data.SetText(text);
                                Clipboard.SetContent(data);
                                Clipboard.Flush();
                                PasteClipboardAndSendF12();
                            });
                        }
                    }
                }
                else if (vkCode == VK_F9 || vkCode == VK_F10 || vkCode == VK_F11 || vkCode == VK_F12)
                {
                    bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                    if (ctrl || shift)
                    {
                        string prefix = ctrl ? "Ctrl" : "Shift";
                        string key = vkCode switch
                        {
                            VK_F9 => $"{prefix}F9Exam",
                            VK_F10 => $"{prefix}F10Exam",
                            VK_F11 => $"{prefix}F11Exam",
                            VK_F12 => $"{prefix}F12Exam",
                            _ => string.Empty
                        };
                        if (!string.IsNullOrEmpty(key))
                        {
                            var exam = AppSettings.Get(key, string.Empty);

                            App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                            {
                                BringMainWindowToForeground();
                                if (App.MainWindow is MainWindow mw)
                                {
                                    var chat = mw.ShowChatPage();
                                    ShowPatientDialogAsync(exam);
                            }
                            });

                        }
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

    }
}
