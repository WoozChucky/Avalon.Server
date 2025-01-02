using Avalon.Client;

public class Program
{
    public static void Main(string[] args)
    {
        Application app = new(1920, 1080);
        app.Setup();
        app.Run();
    }
}
