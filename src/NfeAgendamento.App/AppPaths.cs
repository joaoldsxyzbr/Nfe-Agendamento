namespace NfeAgendamento.App;

public static class AppPaths
{
    public static string LocalDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NfeAgendamento");

    public static string CacheRoot => Path.Combine(LocalDataRoot, "cache");
    public static string StateRoot => Path.Combine(LocalDataRoot, "state");
}
