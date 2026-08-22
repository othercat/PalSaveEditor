namespace PalSaveEditor.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
#if NETFRAMEWORK
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#else
        ApplicationConfiguration.Initialize();
#endif
        Application.Run(new MainForm(args.FirstOrDefault()));
    }
}
