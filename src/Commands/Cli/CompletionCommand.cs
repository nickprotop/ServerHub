// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Commands.Cli;

/// <summary>
/// Command to generate shell completion scripts
/// </summary>
public class CompletionCommand
{
    public static int Execute(string shell)
    {
        var shellLower = shell.ToLowerInvariant();

        switch (shellLower)
        {
            case "bash":
                GenerateBashCompletion();
                return 0;

            case "zsh":
                GenerateZshCompletion();
                return 0;

            case "fish":
                GenerateFishCompletion();
                return 0;

            default:
                Console.Error.WriteLine($"Error: Unsupported shell: {shell}");
                Console.Error.WriteLine("Supported shells: bash, zsh, fish");
                return 1;
        }
    }

    private static void GenerateBashCompletion()
    {
        Console.WriteLine(@"# ServerHub bash completion
# Add to ~/.bashrc: source <(serverhub completion bash)
# Or install: serverhub completion bash | sudo tee /etc/bash_completion.d/serverhub > /dev/null

_serverhub_completions() {
    local cur prev words cword
    _init_completion || return

    # Main commands
    local commands=""marketplace storage test-widget new-widget completion""
    local options=""--widgets-path --dev-mode --discover --verify-checksums --init-config --help --version""

    # If no command yet, complete commands and options
    if [[ $cword -eq 1 ]]; then
        COMPREPLY=( $(compgen -W ""$commands $options"" -- ""$cur"") )
        return
    fi

    # Get the main command
    local cmd=${words[1]}

    case ""$cmd"" in
        marketplace)
            if [[ $cword -eq 2 ]]; then
                COMPREPLY=( $(compgen -W ""search list info install list-installed check-updates update update-all"" -- ""$cur"") )
            fi
            ;;
        storage)
            if [[ $cword -eq 2 ]]; then
                COMPREPLY=( $(compgen -W ""stats cleanup export"" -- ""$cur"") )
            fi
            ;;
        test-widget)
            if [[ $cword -eq 2 ]]; then
                # Complete with .sh files
                COMPREPLY=( $(compgen -f -X '!*.sh' -- ""$cur"") )
            elif [[ ""$cur"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""--extended --ui --skip-confirmation"" -- ""$cur"") )
            fi
            ;;
        new-widget)
            if [[ ""$cur"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""--name --output --list"" -- ""$cur"") )
            fi
            ;;
        completion)
            if [[ $cword -eq 2 ]]; then
                COMPREPLY=( $(compgen -W ""bash zsh fish"" -- ""$cur"") )
            fi
            ;;
    esac
}

complete -F _serverhub_completions serverhub
");
    }

    private static void GenerateZshCompletion()
    {
        Console.WriteLine(@"#compdef serverhub
# ServerHub zsh completion
# Add to ~/.zshrc: source <(serverhub completion zsh)
# Or install: serverhub completion zsh | sudo tee /usr/local/share/zsh/site-functions/_serverhub > /dev/null

_serverhub() {
    local -a commands
    commands=(
        'marketplace:Manage marketplace widgets'
        'storage:Manage storage and database'
        'test-widget:Test and validate widget scripts'
        'new-widget:Interactive widget creation wizard'
        'completion:Generate shell completion scripts'
    )

    local -a marketplace_commands
    marketplace_commands=(
        'search:Search for community widgets'
        'list:List all available widgets'
        'info:Show widget details'
        'install:Install widget from marketplace'
        'list-installed:List installed marketplace widgets'
        'check-updates:Check for widget updates'
        'update:Update widget to latest version'
        'update-all:Update all widgets'
    )

    local -a storage_commands
    storage_commands=(
        'stats:Show database statistics'
        'cleanup:Run database cleanup'
        'export:Export widget data to CSV or JSON'
    )

    _arguments -C \
        '--widgets-path[Override widget directory path]:path:_files -/' \
        '--dev-mode[Enable development mode]' \
        '--discover[Discover and list all available widgets]' \
        '--verify-checksums[Verify bundled widget checksums]' \
        '--init-config[Initialize a new configuration file]:path:_files' \
        '--help[Show help]' \
        '--version[Show version]' \
        '1: :->command' \
        '*:: :->args'

    case $state in
        command)
            _describe 'commands' commands
            ;;
        args)
            case $words[1] in
                marketplace)
                    _describe 'marketplace commands' marketplace_commands
                    ;;
                storage)
                    _describe 'storage commands' storage_commands
                    ;;
                test-widget)
                    _arguments \
                        '1:script:_files -g ""*.sh""' \
                        '--extended[Show extended output]' \
                        '--ui[Show UI preview]' \
                        '--skip-confirmation[Skip confirmation prompts]'
                    ;;
                new-widget)
                    _arguments \
                        '--name[Widget name]:name:' \
                        '--output[Output file path]:path:_files' \
                        '--list[List available templates]'
                    ;;
                completion)
                    _arguments '1:shell:(bash zsh fish)'
                    ;;
            esac
            ;;
    esac
}

_serverhub ""$@""
");
    }

    private static void GenerateFishCompletion()
    {
        Console.WriteLine(@"# ServerHub fish completion
# Install: serverhub completion fish > ~/.config/fish/completions/serverhub.fish

# Main commands
complete -c serverhub -f -n ""__fish_use_subcommand"" -a marketplace -d ""Manage marketplace widgets""
complete -c serverhub -f -n ""__fish_use_subcommand"" -a storage -d ""Manage storage and database""
complete -c serverhub -f -n ""__fish_use_subcommand"" -a test-widget -d ""Test and validate widget scripts""
complete -c serverhub -f -n ""__fish_use_subcommand"" -a new-widget -d ""Interactive widget creation wizard""
complete -c serverhub -f -n ""__fish_use_subcommand"" -a completion -d ""Generate shell completion scripts""

# Global options
complete -c serverhub -l widgets-path -d ""Override widget directory path"" -r -F
complete -c serverhub -l dev-mode -d ""Enable development mode""
complete -c serverhub -l discover -d ""Discover and list all available widgets""
complete -c serverhub -l verify-checksums -d ""Verify bundled widget checksums""
complete -c serverhub -l init-config -d ""Initialize a new configuration file"" -r -F
complete -c serverhub -l help -d ""Show help""
complete -c serverhub -l version -d ""Show version""

# Marketplace subcommands
complete -c serverhub -f -n ""__fish_seen_subcommand_from marketplace"" -a search -d ""Search for community widgets""
complete -c serverhub -f -n ""__fish_seen_subcommand_from marketplace"" -a list -d ""List all available widgets""
complete -c serverhub -f -n ""__fish_seen_subcommand_from marketplace"" -a info -d ""Show widget details""
complete -c serverhub -f -n ""__fish_seen_subcommand_from marketplace"" -a install -d ""Install widget from marketplace""
complete -c serverhub -f -n ""__fish_seen_subcommand_from marketplace"" -a list-installed -d ""List installed marketplace widgets""
complete -c serverhub -f -n ""__fish_seen_subcommand_from marketplace"" -a check-updates -d ""Check for widget updates""
complete -c serverhub -f -n ""__fish_seen_subcommand_from marketplace"" -a update -d ""Update widget to latest version""
complete -c serverhub -f -n ""__fish_seen_subcommand_from marketplace"" -a update-all -d ""Update all widgets""

# Storage subcommands
complete -c serverhub -f -n ""__fish_seen_subcommand_from storage"" -a stats -d ""Show database statistics""
complete -c serverhub -f -n ""__fish_seen_subcommand_from storage"" -a cleanup -d ""Run database cleanup""
complete -c serverhub -f -n ""__fish_seen_subcommand_from storage"" -a export -d ""Export widget data to CSV or JSON""

# Test-widget options
complete -c serverhub -n ""__fish_seen_subcommand_from test-widget"" -l extended -d ""Show extended output""
complete -c serverhub -n ""__fish_seen_subcommand_from test-widget"" -l ui -d ""Show UI preview""
complete -c serverhub -n ""__fish_seen_subcommand_from test-widget"" -l skip-confirmation -d ""Skip confirmation prompts""

# New-widget options
complete -c serverhub -n ""__fish_seen_subcommand_from new-widget"" -l name -d ""Widget name"" -r
complete -c serverhub -n ""__fish_seen_subcommand_from new-widget"" -l output -d ""Output file path"" -r -F
complete -c serverhub -n ""__fish_seen_subcommand_from new-widget"" -l list -d ""List available templates""

# Completion subcommands
complete -c serverhub -f -n ""__fish_seen_subcommand_from completion"" -a ""bash zsh fish"" -d ""Shell type""
");
    }
}
