internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--run-owned-session")
        {
            Console.Error.WriteLine("Explicitly opt into disposable live testing with --run-owned-session. All Inventor processes must be closed first.");
            return 2;
        }
        if (System.Diagnostics.Process.GetProcessesByName("Inventor").Length != 0)
        {
            Console.Error.WriteLine("An Inventor session already exists. No attachment, modification or shutdown attempted.");
            return 2;
        }
        HandlerLive.Run();
        return System.Environment.ExitCode;
    }
}
