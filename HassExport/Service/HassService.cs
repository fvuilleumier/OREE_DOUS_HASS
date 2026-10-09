using System.Text.Json;
using HassExport.Model;
using Microsoft.Playwright;

namespace HassExport.Service;

public class HassService(HassSettings settings)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void AfficherAide()
    {
        Console.WriteLine("""
            ================================================================
             HassExport - à faire la PREMIÈRE fois
            ================================================================
             1. Renseigner l'URL de HA dans appsettings.json (ex: http://192.168.x.x:8123)
             2. dotnet build
             3. Installer Chromium pour Playwright (une seule fois) :
                  pwsh bin/Debug/net10.0/playwright.ps1 install chromium
             4. Mettre "Headless": false, lancer : dotnet run
                -> Chromium s'ouvre : se connecter à Home Assistant
                   (cocher "Rester connecté"). La session est mémorisée
                   dans le dossier ProfileFolder.
             5. Ensuite on peut passer "Headless": true.
             Session expirée ? Supprimer ProfileFolder et refaire l'étape 4.
            ================================================================
            """);
    }

    public async Task ExportAsync()
    {
        var url = settings.Url.Trim();
        if (url.Contains("À_COMPLÉTER") || url.Length == 0)
            throw new InvalidOperationException("Url non renseignée dans appsettings.json.");
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;

        using var playwright = await Playwright.CreateAsync();

        var profile = Path.GetFullPath(settings.ProfileFolder);
        Console.WriteLine($"Profil navigateur : {profile}");

        IBrowserContext context;
        try
        {
            context = await playwright.Chromium.LaunchPersistentContextAsync(
                profile, new BrowserTypeLaunchPersistentContextOptions { Headless = settings.Headless });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist"))
        {
            throw new InvalidOperationException(
                "Chromium n'est pas installé : pwsh bin/Debug/net10.0/playwright.ps1 install chromium");
        }

        await using (context)
        {
            // Nouvel onglet dédié (l'onglet about:blank initial du profil persistant est ignoré)
            var page = context.Pages.First();

            Console.WriteLine($"Navigation vers {url} ...");
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30_000
            });
            Console.WriteLine($"Page chargée : {page.Url}");

            await AttendreHassAsync(page);

            Directory.CreateDirectory(settings.OutputFolder);

            foreach (var key in settings.Keys)
            {
                // Sérialisation côté navigateur : hass contient des objets circulaires (connection, auth...)
                var json = await page.EvaluateAsync<string?>(
                    "k => JSON.stringify(document.querySelector('home-assistant').hass[k] ?? null)", key);

                var pretty = JsonSerializer.Serialize(JsonDocument.Parse(json ?? "null").RootElement, Indented);
                var content = $"// document.querySelector('home-assistant').hass.{key}\n{pretty}\n";

                var file = Path.Combine(settings.OutputFolder, $"{key}.jsonc");
                await File.WriteAllTextAsync(file, content);
                Console.WriteLine($"OK  {file}");
            }
        }
    }

    private async Task AttendreHassAsync(IPage page)
    {
        const string test = "() => !!document.querySelector('home-assistant')?.hass?.states";
        var limite = DateTime.Now.AddMinutes(5);

        while (DateTime.Now < limite)
        {
            try
            {
                if (await page.EvaluateAsync<bool>(test)) return;
            }
            catch (PlaywrightException) { /* page en cours de navigation (login -> accueil) */ }

            if (page.Url.Contains("/auth/"))
            {
                if (settings.Headless)
                    throw new InvalidOperationException(
                        "Session absente ou expirée : repasser \"Headless\": false et se reconnecter.");
                Console.WriteLine(">> Connecte-toi à Home Assistant dans la fenêtre Chromium (coche \"Rester connecté\").");
            }
            else
            {
                Console.WriteLine($"Attente de hass... (page actuelle : {page.Url})");
            }

            await Task.Delay(5_000);
        }

        throw new TimeoutException("hass n'est pas disponible après 5 minutes.");
    }
}
