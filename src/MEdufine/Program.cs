using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace MEdufine;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => a.StartsWith("chrome-extension://", StringComparison.Ordinal)))
        {
            if (!args.Contains("chrome-extension://" + BuildInfo.ExtensionId + "/")) return 2;
            try { NativeHost.RunAsync().GetAwaiter().GetResult(); return 0; } catch { return 1; }
        }
        if (args.Contains("--self-test")) return SelfTest.Run();
        bool smoke = args.Contains("--smoke-test");
        bool demo = args.Contains("--ui-demo");
        using var mutex = new Mutex(true, "Local\\M-Edufine.UI." + BridgeServer.UserKey, out bool created);
        if (!created && !smoke) { MessageBox.Show("M-에듀파인이 이미 실행 중입니다. 작업표시줄에서 열어 주세요.", "M-에듀파인"); return 0; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_,e) => { e.Handled=true; MessageBox.Show("작업을 완료하지 못했습니다. 저장 명령은 자동으로 재시도하지 않습니다. 원본 웹에서 결과를 확인하세요.", "M-에듀파인"); };
        var window = new MainWindow(smoke, demo); app.MainWindow=window;
        if (demo && !smoke) { window.Show(); app.Run(); return Environment.ExitCode; }
        if (smoke) window.ContentRendered += async (_,_) => {
            try {
                await Task.Delay(700);window.UpdateLayout();
                var bmp=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
                bmp.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));
                var dir=Path.Combine(Environment.CurrentDirectory,"artifacts");Directory.CreateDirectory(dir);
                var name=demo?"windows-ui-demo.png":"windows-ui.png";
                using var file=File.Create(Path.Combine(dir,name));png.Save(file);
                File.WriteAllText(Path.Combine(dir,demo?"smoke-test-demo.txt":"smoke-test.txt"),
                    demo?"PASS: Mdir demo UI (synthetic data) created, laid out and rendered. No live login or network."
                        :"PASS: WPF window created, laid out and rendered. No live login or financial action performed.");
            } catch(Exception e) { File.WriteAllText("smoke-error.txt",e.GetType().Name);Environment.ExitCode=1; }
            finally { window.Close(); }
        };
        app.Run(window);return Environment.ExitCode;
    }
}
