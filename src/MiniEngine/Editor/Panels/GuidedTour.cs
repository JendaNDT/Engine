using System.Numerics;
using ImGuiNET;
using System;

namespace MiniEngine.Editor.Panels;

public struct TourRect
{
    public Vector2 Position;
    public Vector2 Size;

    public TourRect(Vector2 pos, Vector2 size)
    {
        Position = pos;
        Size = size;
    }
}

public sealed class GuidedTour
{
    public bool Active { get; set; }
    public int CurrentStep { get; set; }

    public void Start()
    {
        CurrentStep = 0;
        Active = true;
    }

    public void Draw(Vector2 screenSize,
                     TourRect hierarchyRect, TourRect viewportRect, TourRect inspectorRect, TourRect bottomRect, TourRect toolbarRect)
    {
        if (!Active) return;

        var dl = ImGui.GetForegroundDrawList();
        uint overlayColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.65f));

        TourRect currentHighlight = default;
        bool hasHighlight = false;
        string stepTitle = "";
        string stepText = "";
        Vector2 tooltipPos = Vector2.Zero;

        switch (CurrentStep)
        {
            case 0:
                hasHighlight = false;
                stepTitle = "Vítej v průvodci MiniEngine!";
                stepText = "Tato krátká interaktivní prohlídka tě provede rozhraním 3D editoru, ukáže ti základní panely a kde začít výukový kurz.\n\nKlikni na tlačítko 'Další' pro spuštění prohlídky.";
                tooltipPos = new Vector2((screenSize.X - 350f) / 2f, (screenSize.Y - 200f) / 2f);
                break;

            case 1:
                hasHighlight = true;
                currentHighlight = hierarchyRect;
                stepTitle = "1. Hierarchie scény (Levý panel)";
                stepText = "Zde najdeš přehledný seznam všech 3D objektů (entit) v aktuální scéně.\n\nMůžeš na objekty kliknout pro výběr, přejmenovat je, nebo je přetahovat na sebe a tvořit tak rodinné vazby (rodič a dítě).";
                tooltipPos = hierarchyRect.Position + new Vector2(hierarchyRect.Size.X + 15f, 60f);
                break;

            case 2:
                hasHighlight = true;
                currentHighlight = viewportRect;
                stepTitle = "2. 3D Viewport (Střed)";
                stepText = "Hlavní 3D prostor tvé hry.\n\nMůžeš se zde rozhlížet podržením klávesy Alt a tažením levého tlačítka myši.\n\nVybraný objekt se dá posouvat a otáčet pomocí šipek manipulačního Gizma.";
                tooltipPos = viewportRect.Position + new Vector2(25f, viewportRect.Size.Y - 220f);
                break;

            case 3:
                hasHighlight = true;
                currentHighlight = inspectorRect;
                stepTitle = "3. Inspektor vlastností (Pravý panel)";
                stepText = "V tomto panelu vidíš všechny komponenty a parametry označeného objektu.\n\nMůžeš zde nastavit jeho přesnou pozici, změnit barvu, texturu, nebo mu tlačítkem na konci přidat komponenty jako Fyzika, Částice, Zvuk či Chování.";
                tooltipPos = inspectorRect.Position - new Vector2(365f, -60f);
                break;

            case 4:
                hasHighlight = true;
                currentHighlight = bottomRect;
                stepTitle = "4. Prohlížeč souborů a Nástroje";
                stepText = "Spodní část obsahuje Prohlížeč souborů (Asset Browser), kde najdeš dostupné 3D modely (.glb), textury a zvuky.\n\nTyto soubory můžeš jednoduše přetáhnout myší přímo do 3D scény k vytvoření nového objektu.";
                tooltipPos = bottomRect.Position + new Vector2(30f, -190f);
                break;

            case 5:
                hasHighlight = true;
                // Zvýrazníme pravou horní část lišty, kde je tlačítko "Režim výuky"
                currentHighlight = new TourRect(
                    toolbarRect.Position + new Vector2(toolbarRect.Size.X - 300f, 0f),
                    new TourRect(Vector2.Zero, new Vector2(300f, toolbarRect.Size.Y)).Size
                );
                stepTitle = "5. Výukový kurz (Režim výuky)";
                stepText = "Kliknutím na toto tlačítko otevřeš seznam interaktivních úkolů.\n\nTento kurz tě krok za krokem provede základy tvorby 3D světa, částicových efektů, fyziky a zvuků.\n\nUčitel ti může lekce dálkově zpřístupnit nebo přeskočit.";
                tooltipPos = toolbarRect.Position + new Vector2(toolbarRect.Size.X - 370f, toolbarRect.Size.Y + 10f);
                break;

            case 6:
                hasHighlight = false;
                stepTitle = "Prohlídka dokončena!";
                stepText = "Gratuluji, nyní znáš základy rozhraní MiniEngine!\n\nMůžeš kdykoliv změnit svůj profil (nebo režim rozhraní zjednodušený vs. plný) kliknutím na tlačítko 'Můj Profil' v horní liště.\n\nPusť se do své první lekce kliknutím na 'Režim výuky'!";
                tooltipPos = new Vector2((screenSize.X - 350f) / 2f, (screenSize.Y - 200f) / 2f);
                break;
        }

        // Vykreslení tmavého překrytí
        if (hasHighlight)
        {
            float minX = currentHighlight.Position.X;
            float minY = currentHighlight.Position.Y;
            float maxX = currentHighlight.Position.X + currentHighlight.Size.X;
            float maxY = currentHighlight.Position.Y + currentHighlight.Size.Y;

            // Nahoře
            dl.AddRectFilled(new Vector2(0f, 0f), new Vector2(screenSize.X, minY), overlayColor);
            // Dole
            dl.AddRectFilled(new Vector2(0f, maxY), new Vector2(screenSize.X, screenSize.Y), overlayColor);
            // Vlevo
            dl.AddRectFilled(new Vector2(0f, minY), new Vector2(minX, maxY), overlayColor);
            // Vpravo
            dl.AddRectFilled(new Vector2(maxX, minY), new Vector2(screenSize.X, maxY), overlayColor);

            // Zlatý zvýrazňující okraj kolem výřezu
            uint borderColor = ImGui.GetColorU32(new Vector4(0.85f, 0.70f, 0.25f, 1.0f));
            dl.AddRect(new Vector2(minX, minY), new Vector2(maxX, maxY), borderColor, 4f, ImDrawFlags.None, 2f);
        }
        else
        {
            dl.AddRectFilled(new Vector2(0f, 0f), new Vector2(screenSize.X, screenSize.Y), overlayColor);
        }

        // Vykreslení okna nápovědy
        ImGui.SetNextWindowPos(tooltipPos);
        ImGui.SetNextWindowSize(new Vector2(350f, 0f));

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar |
                                 ImGuiWindowFlags.NoResize |
                                 ImGuiWindowFlags.AlwaysAutoResize |
                                 ImGuiWindowFlags.NoMove |
                                 ImGuiWindowFlags.NoSavedSettings;

        ImGui.Begin("TourTooltip", flags);

        // Krok počítadla
        ImGui.TextDisabled($"Krok {CurrentStep + 1} ze 7");
        ImGui.Spacing();

        // Titulek
        ImGui.TextColored(new Vector4(0.85f, 0.70f, 0.25f, 1.0f), stepTitle);
        ImGui.Separator();
        ImGui.Spacing();

        // Text
        ImGui.TextWrapped(stepText);
        ImGui.Spacing();
        ImGui.Spacing();

        // Tlačítka navigace
        if (CurrentStep > 0)
        {
            if (ImGui.Button("Zpět"))
            {
                CurrentStep--;
            }
            ImGui.SameLine();
        }

        if (CurrentStep < 6)
        {
            if (ImGui.Button("Další"))
            {
                CurrentStep++;
            }
            ImGui.SameLine();
        }
        else
        {
            if (ImGui.Button("Dokončit"))
            {
                Active = false;
            }
            ImGui.SameLine();
        }

        if (CurrentStep < 6)
        {
            if (ImGui.Button("Přeskočit"))
            {
                Active = false;
            }
        }

        ImGui.End();
    }
}
