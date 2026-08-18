// -----------------------------------------------------------------------------
//  NEBULA NINE - crew colours, cosmetic catalogue and NPC name generation.
//  Everything here is original content authored for this project.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace Nebula.Core
{
    public static class ColorBank
    {
        /// <summary>Fifteen highly separable suit colours (one per slot).</summary>
        public static readonly Color32[] Suits =
        {
            new Color32(214,  58,  62, 255), // Crimson
            new Color32( 44, 108, 220, 255), // Cobalt
            new Color32( 62, 176,  94, 255), // Fern
            new Color32(243, 192,  46, 255), // Amber
            new Color32(158,  74, 214, 255), // Orchid
            new Color32( 46, 200, 208, 255), // Cyan
            new Color32(238, 124,  40, 255), // Ember
            new Color32(240, 240, 244, 255), // Bone
            new Color32( 60,  66,  82, 255), // Graphite
            new Color32(236, 128, 186, 255), // Blossom
            new Color32(138, 200,  60, 255), // Lime
            new Color32(120,  86,  58, 255), // Umber
            new Color32( 84, 226, 168, 255), // Mint
            new Color32(178,  32, 118, 255), // Magenta
            new Color32(112, 128, 160, 255), // Slate
        };

        public static readonly string[] SuitNames =
        {
            "Crimson", "Cobalt", "Fern", "Amber", "Orchid", "Cyan", "Ember", "Bone",
            "Graphite", "Blossom", "Lime", "Umber", "Mint", "Magenta", "Slate",
        };

        public static Color Get(int index) => Suits[((index % Suits.Length) + Suits.Length) % Suits.Length];

        public static string NameOf(int index) => SuitNames[((index % SuitNames.Length) + SuitNames.Length) % SuitNames.Length];

        /// <summary>Darker variant used for the lower body / shading accents.</summary>
        public static Color Shade(int index, float factor = 0.62f)
        {
            var c = Get(index);
            return new Color(c.r * factor, c.g * factor, c.b * factor, 1f);
        }
    }

    public static class CosmeticBank
    {
        public static readonly string[] Hats =
        {
            "None", "Survey Cap", "Welding Visor", "Beacon Lamp", "Comms Headset",
            "Bio Dome", "Antenna Fin", "Command Beret", "Coolant Halo", "Paper Crown",
        };

        public static readonly string[] Outfits =
        {
            "Standard Suit", "Engineer Rig", "Medical Whites", "Science Coat",
            "Deep Space EVA", "Cargo Harness", "Command Dress", "Maintenance Overalls",
        };

        public static readonly string[] Accessories =
        {
            "None", "Tool Belt", "Sample Case", "Shoulder Drone", "Data Slate",
            "Backpack Tank", "Utility Cable", "Mag Boots",
        };

        public static readonly string[] Trails =
        {
            "None", "Ion Sparks", "Frost Motes", "Ember Wisp", "Static Field", "Nebula Dust",
        };
    }

    public static class NameBank
    {
        private static readonly string[] First =
        {
            "Kova", "Rennick", "Sable", "Halcyon", "Ivo", "Marek", "Tessa", "Nadir",
            "Juno", "Orrin", "Silva", "Bram", "Nika", "Pell", "Quill", "Roon",
            "Vesper", "Wynn", "Ash", "Cyra", "Dax", "Elyn", "Fenn", "Gale",
            "Hux", "Isla", "Jory", "Kest", "Lyra", "Mox", "Nyx", "Osk",
            "Pax", "Rhea", "Sten", "Thal", "Ulla", "Vero", "Wren", "Zed",
        };

        private static readonly string[] Tags =
        {
            "", "", "", "", "_7", "-9", "77", "x", "_v2", "01", "42", "zz",
        };

        private static readonly string[] Handles =
        {
            "voidrunner", "coolant", "hexline", "starboard", "driftwake", "loworbit",
            "reactorpop", "quietvent", "nullsignal", "greybox", "sixthdeck", "arclight",
            "solarflare", "deadcircuit", "orbitcat", "mainframe", "wiretrace", "hullbreach",
        };

        public static string RandomHumanName()
        {
            return Handles[Random.Range(0, Handles.Length)] + Tags[Random.Range(0, Tags.Length)];
        }

        public static string RandomNpcName(System.Random rng)
        {
            bool handle = rng.NextDouble() < 0.45;
            string body = handle
                ? Handles[rng.Next(Handles.Length)]
                : First[rng.Next(First.Length)];
            string tag = Tags[rng.Next(Tags.Length)];
            if (!handle && rng.NextDouble() < 0.3) body = body.ToUpperInvariant();
            return body + tag;
        }
    }
}
