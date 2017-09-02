using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace filv
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        struct ItemInfo
        {
            public ushort ItemLevel;
            public bool isTwohand;
        }

        private readonly IntPtr m_handle;

        private IDictionary<uint, ItemInfo> m_items = new SortedDictionary<uint, ItemInfo>();

        private Process m_ffxiv;
        private IntPtr m_ffxivWindows;

        public MainWindow()
        {
            InitializeComponent();

            var wih = new WindowInteropHelper(this);
            wih.EnsureHandle();
            this.m_handle = wih.Handle;

            int exStyle = NativeMethods.GetWindowLong(this.m_handle, GWL_EXSTYLE);
            NativeMethods.SetWindowLong(this.m_handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.lbl.Text = "";
            this.lblName.Text = "업데이트중";

            try
            {
                this.m_ffxiv = Process.GetProcessesByName("ffxiv_dx11")[0];
                this.m_ffxivWindows = this.m_ffxiv.MainWindowHandle;
            }
            catch
            {
                try
                {
                    this.m_ffxiv = Process.GetProcessesByName("ffxiv")[0];
                    this.m_ffxivWindows = this.m_ffxiv.MainWindowHandle;
                }
                catch
                {
                    MessageBox.Show("파이널 판타지 14 를 실행해주세요.");
                    App.Current.Shutdown();
                    return;
                }
            }
            
            var path = Path.ChangeExtension(System.Reflection.Assembly.GetExecutingAssembly().Location, ".dat");
            if (!await Task.Run(() => DownloadItemData(path)))
            {
                MessageBox.Show("새 아이템 정보를 불러오지 못했습니다.");
                App.Current.Shutdown();
                return;
            }
            if (!await Task.Run(() => ReadData(path)))
            {
                File.Delete(path);

                MessageBox.Show("아이템 정보를 불러오지 못했습니다.");
                App.Current.Shutdown();
                return;
            }

            var net = new FFXIVApp.Network();
            net.StartCapture(this.m_ffxiv);
            net.HandleMessageEvent += this.Net_HandleMessageEvent;

            var tmr = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.ApplicationIdle, this.AutoHide, this.Dispatcher);

            this.lbl.Text = "대기중";
            this.lblName.Text = "-";
        }

        private bool DownloadItemData(string path)
        {
            try
            {
                using (var wc = new WebClientWithMethod())
                {
                    var fileInfo = new FileInfo(path);
                    if (fileInfo.Exists)
                    {
                        wc.Method = "HEAD";
                        wc.DownloadData("https://raw.githubusercontent.com/RyuaNerin/filv/master/item.exh_ko.csv.gz");

                        try
                        {
                            var len = long.Parse(wc.ResponseHeaders[HttpResponseHeader.ContentLength]);

                            if (len == fileInfo.Length)
                                return true;
                        }
                        catch
                        {
                        }
                    }

                    wc.Method = null;

                    wc.DownloadProgressChanged += (s, e) => this.Dispatcher.Invoke(new Action(() => this.lbl.Text = e.ProgressPercentage + " %"));

                    wc.DownloadFileAsync(new Uri("https://raw.githubusercontent.com/RyuaNerin/filv/master/item.exh_ko.csv.gz"), path);
                    while (wc.IsBusy) Thread.Sleep(100);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ReadData(string path)
        {
            try
            {
                using (var file = File.OpenRead(path))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress, true))
                using (var trd = new StreamReader(gzip, Encoding.UTF8, true))
                using (var reader = new CsvHelper.CsvReader(trd))
                {
                    reader.Read();

                    while (reader.Read())
                    {
                        var th = reader.GetField<uint>(28);     // AC
                        if (th == 0) continue;

                        var id = reader.GetField<uint>(0);      // A
                        var lv = reader.GetField<ushort>(12);   // M

                        this.m_items.Add(id, new ItemInfo { ItemLevel = lv, isTwohand = (th == 13) });
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void Net_HandleMessageEvent(byte[] data)
        {
            int totalLv = 0;
            int weaponLv = 0;

            for (int i = 0; i < 13; ++i)
            {
                var itemId = BitConverter.ToUInt32(data, 96 + 40 * i);

                if (itemId == 0) continue;
                if (!this.m_items.ContainsKey(itemId))
                {
                    this.Dispatcher.Invoke(new Action(() => this.lbl.Text = "?"));
                    break;
                }

                var info = this.m_items[itemId];

                if (i == 0) weaponLv = info.ItemLevel;
                totalLv += info.isTwohand ? info.ItemLevel * 2 : info.ItemLevel;
            }

            var nick = ParseName(data);

            totalLv /= 13;
            this.Dispatcher.Invoke(new Action(() => {
                this.lbl.Text = weaponLv + " - " + totalLv;
                this.lblName.Text = nick;
            }));
        }

        private string ParseName(byte[] data)
        {
            int len = 0;
            while (656 + len < data.Length && data[656 + len] != 0)
                ++len;

            return Encoding.UTF8.GetString(data, 656, len);
        }

        private void lbl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(this.m_handle, WM_NCLBUTTONDOWN, new IntPtr(HT_CAPTION), IntPtr.Zero);
            }
            else if (e.ChangedButton == MouseButton.Right)
                App.Current.Shutdown();
        }

        private bool m_hiddenNow = false;
        private void AutoHide(object sender, EventArgs e)
        {
            try
            {
                var hwnd = NativeMethods.GetForegroundWindow();

                if (this.m_hiddenNow)
                {
                    if (hwnd == this.m_handle || hwnd == this.m_ffxivWindows)
                    {
                        this.Show();
                        this.m_hiddenNow = false;
                    }
                }
                else
                {
                    if (hwnd != this.m_handle && hwnd != this.m_ffxivWindows)
                    {
                        this.Hide();
                        this.m_hiddenNow = true;
                    }
                }
            }
            catch
            {
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern bool ReleaseCapture();

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            public static extern int GetWindowLong(IntPtr hwnd, int index);

            [DllImport("user32.dll")]
            public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        }

        class WebClientWithMethod : WebClient
        {
            public string Method
            {
                get;
                set;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                var req = base.GetWebRequest(address) as HttpWebRequest;
                req.UserAgent = "filv";

                if (!string.IsNullOrEmpty(this.Method)) req.Method = this.Method;

                return req;
            }
        }

        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int WM_NCLBUTTONDOWN = 0xA1;

        private const int HT_CAPTION = 0x2;

        private const int GWL_EXSTYLE = -20;
    }
}
