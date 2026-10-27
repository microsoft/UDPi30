using System.CommandLine;
using Spectre.Console;
using Udpi30.Core.Engine;
using static Udpi30.Cli.CliHelpers;

namespace Udpi30.Cli;

internal static partial class CliApp
{
    // -- admin (tenant settings) ---------------------------------------------

    /// <summary>
    /// The <c>admin</c> command group manages tenant-level settings via the Fabric
    /// Admin API. These are tenant-wide (not per-workspace) and high blast-radius,
    /// so they live outside the normal resource <c>deploy</c> flow and are applied
    /// only through the explicit, gated <c>admin apply</c>.
    /// </summary>
    private static Command AdminCommand()
    {
        var cmd = new Command("admin", "Manage tenant-level (admin) settings.");
        cmd.Subcommands.Add(AdminPlanCommand());
        cmd.Subcommands.Add(AdminApplyCommand());
        return cmd;
    }

    private static Command AdminPlanCommand()
    {
        var file = FileOption();
        var cmd = new Command("plan", "Preview tenant setting changes against the live tenant (read-only).") { file };

        cmd.SetAction(pr =>
        {
            if (!TryLoadAdmin(pr.GetValue(file), out var deployment))
            {
                return 1;
            }
            if (deployment!.Admin.TenantSettings.Count == 0)
            {
                Ansi.MarkupLine("[dim]No admin.tenant_settings declared in this deployment.[/]");
                return 0;
            }

            if (!TryPlanAdmin(deployment, out var plan))
            {
                return 1;
            }

            DisplayAdminPlan(plan!);
            return 0;
        });
        return cmd;
    }

    private static Command AdminApplyCommand()
    {
        var file = FileOption();
        var dryRun = new Option<bool>("--dry-run") { Description = "Preview without applying" };
        var autoApprove = new Option<bool>("--auto-approve", "-y") { Description = "Skip confirmation" };

        var cmd = new Command("apply", "Apply declared tenant settings to the tenant (Fabric admin required).")
        { file, dryRun, autoApprove };

        cmd.SetAction(pr =>
        {
            if (!TryLoadAdmin(pr.GetValue(file), out var deployment))
            {
                return 1;
            }
            if (deployment!.Admin.TenantSettings.Count == 0)
            {
                Ansi.MarkupLine("[dim]No admin.tenant_settings declared in this deployment.[/]");
                return 0;
            }

            if (!TryPlanAdmin(deployment, out var plan))
            {
                return 1;
            }

            DisplayAdminPlan(plan!);

            if (plan!.HasUnknown)
            {
                Ansi.WriteLine();
                Error("One or more settingNames were not found in this tenant. Fix the names before applying.");
                Ansi.MarkupLine("  Setting names are the API identifiers (e.g. [bold]PublishToWeb[/]), not the portal titles.");
                return 1;
            }

            if (!plan.HasChanges)
            {
                Ansi.WriteLine();
                Ansi.MarkupLine("[bold green]Tenant settings already match — nothing to apply.[/]");
                return 0;
            }

            var dry = pr.GetValue(dryRun);
            if (!dry && !pr.GetValue(autoApprove))
            {
                Ansi.WriteLine();
                Ansi.MarkupLine("[bold red]WARNING:[/] These changes apply tenant-wide and affect every user in the organization.");
                if (!Confirm("Apply these tenant setting changes?"))
                {
                    Ansi.MarkupLine("[dim]Cancelled.[/]");
                    return 0;
                }
            }

            var client = NewClient();
            Ansi.WriteLine();
            var (applied, failed) = AdminApplier.Apply(client, plan, Ansi, dry);
            Ansi.WriteLine();
            if (dry)
            {
                Ansi.MarkupLine("[dim]Dry-run complete — no changes made.[/]");
                return 0;
            }
            if (failed > 0)
            {
                Ansi.MarkupLine($"[bold red]Apply completed with errors.[/] Updated: {applied}, Failed: {failed}");
                return 1;
            }
            Ansi.MarkupLine($"[bold green]Apply complete.[/] Updated: {applied} setting(s).");
            Ansi.MarkupLine("[dim]Note: tenant setting changes can take up to 15 minutes to take effect.[/]");
            return 0;
        });
        return cmd;
    }

    private static bool TryLoadAdmin(string? file, out Core.Models.DeploymentDefinition? deployment)
    {
        try
        {
            deployment = Loader.LoadDeployment(file);
            return true;
        }
        catch (DeploymentLoadError e)
        {
            Error(e.Message);
            deployment = null;
            return false;
        }
    }

    private static bool TryPlanAdmin(Core.Models.DeploymentDefinition deployment, out AdminPlan? plan)
    {
        try
        {
            var client = NewClient();
            plan = AdminApplier.Plan(client, deployment.Admin);
            return true;
        }
        catch (Exception e)
        {
            Error($"Could not read tenant settings: {e.Message}");
            Ansi.MarkupLine("  This requires a Fabric administrator or a service principal with [bold]Tenant.ReadWrite.All[/].");
            plan = null;
            return false;
        }
    }

    private static void DisplayAdminPlan(AdminPlan plan)
    {
        Ansi.MarkupLine("[bold]Tenant Settings Plan[/]");
        Ansi.WriteLine();

        var update = 0;
        var nochange = 0;
        var unknown = 0;

        foreach (var item in plan.Items)
        {
            switch (item.Kind)
            {
                case AdminChangeKind.Update:
                    update++;
                    Ansi.MarkupLine($"  [yellow]~[/] [bold]{Markup.Escape(item.SettingName)}[/]  [dim]{Markup.Escape(item.Title ?? "")}[/]");
                    foreach (var change in item.Changes)
                    {
                        Ansi.MarkupLine($"      {Markup.Escape(change)}");
                    }
                    break;
                case AdminChangeKind.Unknown:
                    unknown++;
                    Ansi.MarkupLine($"  [red]![/] [bold]{Markup.Escape(item.SettingName)}[/]  [red](unknown setting)[/]");
                    foreach (var change in item.Changes)
                    {
                        Ansi.MarkupLine($"      [red]{Markup.Escape(change)}[/]");
                    }
                    break;
                default:
                    nochange++;
                    Ansi.MarkupLine($"  [dim]=[/] {Markup.Escape(item.SettingName)}  [dim]no change[/]");
                    break;
            }

            foreach (var warning in item.Warnings)
            {
                Ansi.MarkupLine($"      [yellow]warning:[/] {Markup.Escape(warning)}");
            }
        }

        Ansi.WriteLine();
        Ansi.MarkupLine($"  Summary: {update} to update, {nochange} unchanged" + (unknown > 0 ? $", [red]{unknown} unknown[/]" : ""));
    }
}
