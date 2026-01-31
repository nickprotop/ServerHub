// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;

namespace ServerHub.Commands.Services;

public class TemplateSubstitution
{
    // Built-in variables
    private static readonly Dictionary<string, Func<string>> BuiltInVariables = new()
    {
        ["USER"] = () => Environment.UserName,
        ["DATE"] = () => DateTime.Now.ToString("yyyy-MM-dd"),
        ["DATETIME"] = () => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    };

    public string Substitute(string content, Dictionary<string, string> variables, string outputFile = "")
    {
        var allVariables = new Dictionary<string, string>(variables);

        // Add built-in variables
        foreach (var (key, valueFunc) in BuiltInVariables)
        {
            if (!allVariables.ContainsKey(key))
                allVariables[key] = valueFunc();
        }

        // Add OUTPUT_FILE if provided
        if (!string.IsNullOrEmpty(outputFile))
        {
            allVariables["OUTPUT_FILE"] = outputFile;
            allVariables["OUTPUT_FILE_NAME"] = Path.GetFileName(outputFile);
            allVariables["OUTPUT_FILE_DIR"] = Path.GetDirectoryName(outputFile) ?? ".";
        }

        // Multi-pass substitution for nested variables
        string result = content;
        int maxPasses = 3;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            bool changed = false;
            foreach (var (key, value) in allVariables)
            {
                var placeholder = $"{{{{{key}}}}}";
                if (result.Contains(placeholder))
                {
                    result = result.Replace(placeholder, value);
                    changed = true;
                }
            }
            if (!changed) break;
        }

        return result;
    }

    public List<string> FindUnsubstitutedVariables(string content)
    {
        var regex = new Regex(@"\{\{(\w+)\}\}");
        var matches = regex.Matches(content);
        return matches.Select(m => m.Groups[1].Value).Distinct().ToList();
    }
}
