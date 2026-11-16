// Writes the plugin's TriggerCatalog, item kinds, and variables as JSON for the screenshot harness's stand-in Catalog endpoint.
using System.Text.Json;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using Jellyfin.Data.Enums;
var triggers = TriggerCatalog.All.Where(t => t.Selectable).Select(t => new { Key = t.Trigger.ToString(), t.Label, t.Category, t.Description, t.HasItem, t.HasSession, t.HasUser, Variables = t.Variables.Select(v => new { v.Name, v.Description }) });
var fields = Enum.GetValues<ConditionField>().Select(f => new { Key = f.ToString(), Variable = TriggerCatalog.VariableFor(f), Triggers = TriggerCatalog.All.Where(t => TriggerCatalog.Supplies(t.Trigger, f)).Select(t => t.Trigger.ToString()), ForEach = new[] { ForEachMode.Items, ForEachMode.Users }.Where(m => TriggerCatalog.ForEachVariables(m).Any(v => v.Name == TriggerCatalog.VariableFor(f))).Select(m => m.ToString()) });
var o = new { Triggers = triggers, Fields = fields,
  ItemKinds = Enum.GetValues<BaseItemKind>().Select(k => new { Value = k.ToString(), Label = ItemKindLabels.For(k) }).OrderBy(k => k.Label, StringComparer.OrdinalIgnoreCase),
  ForEachVariables = new { Items = TriggerCatalog.ForEachVariables(ForEachMode.Items).Select(v => new { v.Name, v.Description }), Users = TriggerCatalog.ForEachVariables(ForEachMode.Users).Select(v => new { v.Name, v.Description }) },
  FailureVariables = TriggerCatalog.FailureVariables.Select(v => new { v.Name, v.Description }) };
File.WriteAllText(args.Length > 0 ? args[0] : "catalog.json", JsonSerializer.Serialize(o) + "\n");
