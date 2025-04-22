using System.Text.RegularExpressions;

namespace MatrixBridgeSdk.Helpers;

public static class MatrixIdValidator
{
    // Precompiled regex for performance
    private static readonly Regex MatrixIdRegex = new Regex(
        @"^@([a-z0-9.\-_=/\+]+):([^\s:]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Validate a Matrix Id (MxId)
    /// </summary>
    /// <param name="input">Id to validate</param>
    /// <returns>true if valid</returns>
    public static bool IsValidMatrixId(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        // Check length constraint
        if (input.Length > 255)
            return false;

        return MatrixIdRegex.Match(input).Success;
    }
}