using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Register_CheckerTest
{
    public sealed class Form1 : Form
    {
        private readonly Color _background = Color.FromArgb(17, 22, 32);
        private readonly Color _panel = Color.FromArgb(26, 33, 46);
        private readonly Color _muted = Color.FromArgb(155, 169, 190);
        private readonly Color _accent = Color.FromArgb(91, 221, 171);
        private readonly Button _load = new ReadableButton(), _start = new ReadableButton(), _stop = new ReadableButton(), _save = new ReadableButton(), _folder = new ReadableButton(), _reset = new ReadableButton();
        private readonly NumericUpDown _workers = new NumericUpDown(), _timeout = new NumericUpDown(), _attempts = new NumericUpDown();
        private readonly Label _input = new Label(), _status = new Label(), _progressText = new Label(), _elapsed = new Label(), _rate = new Label();
        private readonly Label[] _numbers = new Label[5];
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch _watch = new Stopwatch();
        private ScanTarget[] _targets = new ScanTarget[0];
        private ScanSession _session;
        private CancellationTokenSource _cancel;
        private Task _operation;
        private bool _busy, _closing;

        public Form1()
        {
            Text = "NLA Checker | KillerTM | v0.2.0";
            ClientSize = new Size(1020, 680);
            MinimumSize = new Size(1036, 719);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = _background;
            ForeColor = Color.FromArgb(234, 240, 248);
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }
            BuildLayout();
            _timer.Interval = 250;
            _timer.Tick += (s, e) => RefreshStats();
            _load.Click += async (s, e) => await LoadFromDialogAsync();
            _start.Click += async (s, e) => await StartAsync();
            _stop.Click += (s, e) => { if (_cancel != null) _cancel.Cancel(); _stop.Enabled = false; _status.Text = "Stopping safely..."; };
            _save.Click += (s, e) => SavePending();
            _folder.Click += (s, e) => OpenResults();
            _reset.Click += (s, e) => { _targets = new ScanTarget[0]; _session = null; _watch.Reset(); _input.Text = "No list loaded. Choose a text file to begin."; _status.Text = "Ready"; RefreshStats(); SetBusy(false); };
            AllowDrop = true;
            DragEnter += (s, e) => e.Effect = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += async (s, e) => { var files = (string[])e.Data.GetData(DataFormats.FileDrop); if (!_busy && files != null && files.Length > 0) await LoadFileAsync(files[0]); };
            FormClosing += OnClosing;
            FormClosed += (s, e) => _timer.Dispose();
            SetBusy(false);
            RefreshStats();
        }

        private Label LabelAt(string text, int x, int y, int width, int height, float size, Color color, Control parent = null)
        {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = new Font("Segoe UI", size), ForeColor = color };
            (parent ?? this).Controls.Add(label);
            return label;
        }
        private void ButtonAt(Button button, string text, int x, int y, int width, bool primary = false)
        {
            button.Text = text; button.SetBounds(x, y, width, 42); button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0; button.BackColor = primary ? Color.FromArgb(29, 112, 84) : _panel;
            button.ForeColor = Color.White; button.Cursor = Cursors.Hand;
            Controls.Add(button);
        }
        private void NumberAt(NumericUpDown field, string label, int x, int min, int max, int value)
        {
            LabelAt(label, x, 184, 210, 25, 10, _muted);
            field.SetBounds(x, 215, 180, 32); field.Minimum = min; field.Maximum = max; field.Value = value;
            field.BackColor = _panel; field.ForeColor = ForeColor; field.Font = new Font("Segoe UI", 12F);
            Controls.Add(field);
        }
        private void BuildLayout()
        {
            LabelAt("KILLERTM  /  NETWORK TOOLS", 32, 24, 600, 24, 9, _accent);
            LabelAt("NLA Checker", 30, 54, 650, 54, 29, ForeColor);
            LabelAt("RDP negotiation, clear results.", 33, 112, 700, 26, 12, _muted);
            LabelAt("v0.2.0", 900, 66, 95, 30, 12, _accent);
            NumberAt(_workers, "Concurrent checks", 34, 1, 512, 64);
            NumberAt(_timeout, "Timeout per attempt (seconds)", 272, 1, 120, 5);
            NumberAt(_attempts, "Total attempts", 510, 1, 10, 1);
            ButtonAt(_load, "Load IP list", 760, 207, 224);
            _input.SetBounds(34, 262, 950, 32); _input.ForeColor = _muted; _input.AutoEllipsis = true;
            _input.Text = "No list loaded. Choose a text file to begin."; Controls.Add(_input);
            string[] names = { "NLA / CredSSP", "Standard / TLS", "Negotiation failed", "Not RDP", "Unreachable" };
            Color[] colors = { _accent, Color.FromArgb(113, 180, 255), Color.FromArgb(241, 185, 104), _muted, Color.FromArgb(242, 132, 132) };
            for (int i = 0; i < names.Length; i++)
            {
                var card = new Panel { BackColor = _panel, Location = new Point(34 + i * 193, 310), Size = new Size(178, 112) };
                Controls.Add(card);
                LabelAt(names[i], 14, 14, 155, 24, 9, _muted, card);
                _numbers[i] = LabelAt("0", 12, 44, 158, 52, 26, colors[i], card);
            }
            _progressText.SetBounds(34, 449, 610, 28); Controls.Add(_progressText);
            _elapsed.SetBounds(702, 449, 280, 28); _elapsed.TextAlign = ContentAlignment.TopRight; _elapsed.ForeColor = _muted; Controls.Add(_elapsed);
            _progress.SetBounds(34, 484, 950, 12); Controls.Add(_progress);
            _status.SetBounds(34, 510, 690, 29); _status.Text = "Ready"; _status.ForeColor = _accent; _status.AutoEllipsis = true; Controls.Add(_status);
            _rate.SetBounds(735, 510, 250, 29); _rate.TextAlign = ContentAlignment.TopRight; _rate.ForeColor = _muted; Controls.Add(_rate);
            ButtonAt(_start, "Start check", 34, 559, 190, true);
            ButtonAt(_stop, "Stop", 236, 559, 110);
            ButtonAt(_save, "Save unchecked", 358, 559, 190);
            ButtonAt(_folder, "Open results", 560, 559, 180);
            ButtonAt(_reset, "Reset", 752, 559, 110);
            LabelAt("IPv4 / IPv6  •  Drop a .txt list here  •  Results stay on your computer", 34, 627, 950, 25, 9, _muted);
        }
        private void SetBusy(bool busy)
        {
            _busy = busy;
            _load.Enabled = _workers.Enabled = _timeout.Enabled = _attempts.Enabled = _reset.Enabled = !busy;
            _start.Enabled = !busy && _targets.Length > 0;
            _stop.Enabled = busy;
            _save.Enabled = !busy && _session != null && _session.Checked < _session.Total;
            _folder.Enabled = _session != null && Directory.Exists(_session.OutputDirectory);
        }
        private async Task LoadFromDialogAsync()
        {
            using (var dialog = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", Title = "Load IP list" })
                if (dialog.ShowDialog(this) == DialogResult.OK) await LoadFileAsync(dialog.FileName);
        }
        private async Task LoadFileAsync(string path)
        {
            if (_busy) return;
            SetBusy(true); _status.Text = "Reading and validating list...";
            _cancel = new CancellationTokenSource();
            try
            {
                var task = Task.Run(() => InputList.Load(path, _cancel.Token));
                _operation = task;
                var input = await task;
                _targets = input.Targets; _session = null; _watch.Reset();
                _input.Text = string.Format("{0}  |  {1:N0} unique  ·  {2:N0} duplicates  ·  {3:N0} invalid  ·  {4:N0} blank/comments", Path.GetFileName(path), _targets.Length, input.Duplicates, input.Invalid, input.Ignored);
                _status.Text = _targets.Length == 0 ? "No valid IP addresses found." : "List ready";
                RefreshStats();
            }
            catch (OperationCanceledException) { _status.Text = "Loading cancelled"; }
            catch (Exception ex) { ShowError("Could not load the list", ex); }
            finally { _operation = null; _cancel.Dispose(); _cancel = null; SetBusy(false); }
        }
        private async Task StartAsync()
        {
            if (_busy || _targets.Length == 0) return;
            SetBusy(true); _cancel = new CancellationTokenSource();
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerTM", "NLA Checker", "Results", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            _session = new ScanSession(_targets, (int)_workers.Value, (int)_timeout.Value * 1000, (int)_attempts.Value, directory);
            _watch.Restart(); _timer.Start(); _status.Text = "Checking RDP negotiation...";
            try
            {
                _operation = Task.Run(() => _session.RunAsync(_cancel.Token));
                await _operation;
                _status.Text = _cancel.IsCancellationRequested ? "Stopped. Unchecked addresses saved." : "Finished. Results saved.";
            }
            catch (Exception ex) { ShowError("Check stopped because of an error. Review the results folder", ex); }
            finally
            {
                _watch.Stop(); _timer.Stop(); RefreshStats();
                _operation = null; _cancel.Dispose(); _cancel = null; SetBusy(false);
            }
        }
        private void RefreshStats()
        {
            int total = _session == null ? _targets.Length : _session.Total;
            int done = _session == null ? 0 : _session.Checked;
            for (int i = 0; i < _numbers.Length; i++) _numbers[i].Text = (_session == null ? 0 : _session.Count((RdpStatus)i)).ToString("N0");
            _progress.Maximum = Math.Max(1, total); _progress.Value = Math.Min(done, _progress.Maximum);
            _progressText.Text = string.Format("{0:N0} / {1:N0} checked     ·     {2:N0} remaining", done, total, total - done);
            _elapsed.Text = "Elapsed  " + _watch.Elapsed.ToString(@"hh\:mm\:ss");
            _rate.Text = (_watch.Elapsed.TotalSeconds > 0 ? done / _watch.Elapsed.TotalSeconds : 0).ToString("N1") + " checks / sec";
        }
        private void SavePending()
        {
            if (_busy || _session == null) return;
            using (var dialog = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt", FileName = "Not_Checked.txt" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try { _session.SaveUnchecked(dialog.FileName); _status.Text = "Unchecked list saved."; }
                    catch (Exception ex) { ShowError("Could not save the list", ex); }
        }
        private void OpenResults()
        {
            if (_session == null) return;
            try { Process.Start(new ProcessStartInfo(_session.OutputDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError("Could not open results", ex); }
        }
        private void ShowError(string title, Exception ex)
        {
            _status.Text = title;
            if (!_closing) MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        private async void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!_busy) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            if (_cancel != null) _cancel.Cancel();
            var pending = _operation;
            if (pending != null) try { await pending; } catch { /* The operation's UI handler reports errors. */ }
            BeginInvoke(new Action(Close));
        }
    }

    internal sealed class ReadableButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled) { base.OnPaint(e); return; }
            e.Graphics.Clear(BackColor);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
