internal static partial class Helpers
{
    /// <summary>
    /// Retrieves the value of an environment variable from the current process.
    /// </summary>
    /// <param name="variable">The name of the environment variable.</param>
    /// <returns>The value of the environment variable specified by <paramref name="variable"/>,
    /// or <see langword="null"/> if the environment variable is not found.</returns>
    private static string? TryGetEnvironmentVariable(string variable)
    {
        try
        {
            return Environment.GetEnvironmentVariable(variable);
        }
        catch
        {
            return null;
        }
    }
}
