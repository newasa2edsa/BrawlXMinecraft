using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomBrawlServer.Logic;

/// <summary>
/// Card (class 23) IDs for star powers and gadgets.
///
/// Source: the ID set comes from the official Brawl Stars data IDs (23000000 + card id),
/// taken from the file names in the Brawlify CDN repo (gadgets/regular, star-powers/regular).
/// Cards are numbered in release order, so everything up to <see cref="DefaultMaxCardId"/> exists
/// in a client that ships brawler 108 (the last entry of <see cref="BrawlerRegistry"/>).
/// Anything above that cutoff belongs to brawlers newer than the v68 client and is filtered out,
/// because sending a card the client has no data for can crash it.
///
/// Hypercharge cards and gears are NOT in here (their card ids are not published anywhere I could
/// reach); see README "Not implemented".
/// </summary>
public static class CardRegistry
{
    /// <summary>Highest gadget/star power card id that exists in the v68 client data.</summary>
    public const int DefaultMaxCardId = 1321;

    /// <summary>Data-reference class id used for cards (same class the unlock cards use).</summary>
    public const int CardClassId = 23;

    private static readonly int[] AllStarPowers =
    {
        76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91,
        92, 93, 94, 99, 104, 109, 114, 119, 124, 129, 134, 135, 136, 137, 138, 139,
        140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155,
        156, 157, 158, 159, 160, 161, 168, 181, 186, 187, 192, 193, 198, 199, 204, 205,
        210, 211, 222, 223, 228, 229, 234, 235, 240, 241, 283, 284, 300, 301, 307, 308,
        324, 325, 331, 332, 338, 339, 345, 346, 362, 363, 369, 370, 376, 377, 383, 384,
        390, 391, 397, 398, 414, 415, 421, 422, 431, 432, 438, 439, 452, 453, 470, 471,
        478, 479, 495, 496, 503, 504, 511, 512, 519, 520, 527, 528, 535, 536, 543, 544,
        553, 554, 561, 562, 569, 570, 577, 578, 585, 586, 593, 594, 601, 602, 609, 610,
        623, 624, 637, 638, 646, 647, 659, 660, 667, 668, 675, 676, 734, 735, 752, 753,
        764, 765, 772, 773, 804, 805, 815, 816, 832, 833, 848, 849, 875,
        876, 883, 884, 905, 906, 915, 916, 929, 930, 938, 939, 989, 990, 998, 999, 1039,
        1040, 1047, 1048, 1060, 1061, 1068, 1069, 1181, 1182, 1189, 1190, 1216, 1217, 1245, 1246, 1253,
        1254, 1279, 1280, 1295, 1296, 1318, 1319
    };

    private static readonly int[] AllGadgets =
    {
        242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255, 256, 257,
        258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273,
        274, 275, 276, 277, 278, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 295,
        302, 309, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 326, 333, 340, 347,
        348, 349, 350, 351, 352, 353, 355, 356, 364, 371, 378, 385, 392, 399,
        400, 401, 402, 403, 404, 405, 406, 407, 408, 409, 416, 423, 433, 440, 454,
        455, 456, 457, 458, 459, 460, 461, 462, 463, 464, 465, 472, 473, 480, 481, 485,
        486, 487, 488, 489, 490, 497, 498, 505, 506, 513, 514, 521, 522, 529, 530, 537,
        538, 545, 546, 555, 556, 563, 564, 571, 572, 579, 580, 587, 588, 595, 596, 603,
        604, 611, 612, 625, 626, 639, 640, 648, 649, 661, 662, 669, 670, 677, 678, 736,
        737, 754, 755, 766, 767, 774, 775, 806, 807, 817, 818, 834, 835, 850, 851,
        877, 878, 885, 886, 907, 908, 917, 918, 931, 932, 940, 941, 991, 992, 1000, 1001,
        1041, 1042, 1049, 1050, 1062, 1063, 1070, 1071, 1183, 1184, 1191, 1192, 1218, 1219, 1247, 1248,
        1255, 1256, 1281, 1282, 1297, 1298, 1320, 1321
    };

    private static readonly HashSet<int> StarPowerSet = new(AllStarPowers);
    private static readonly HashSet<int> GadgetSet = new(AllGadgets);

    // Newer brawlers (about half of the roster) lay their cards out as:
    //   unlock = u, star powers = u+4 / u+5, gadgets = u+6 / u+7
    // Older brawlers were given their abilities in later batches and are scattered, so they are
    // not attributable by position and map to -1 (callers fall back to the selected brawler).
    private static readonly Dictionary<int, int> CardToBrawler = BuildCardToBrawler();

    private static Dictionary<int, int> BuildCardToBrawler()
    {
        var map = new Dictionary<int, int>();
        foreach (var (characterId, unlockCard) in BrawlerRegistry.CharacterToUnlockCard)
        {
            var sp = new[] { unlockCard + 4, unlockCard + 5 };
            var gd = new[] { unlockCard + 6, unlockCard + 7 };
            if (!sp.All(StarPowerSet.Contains) || !gd.All(GadgetSet.Contains))
                continue;

            foreach (var id in sp.Concat(gd))
                map[id] = characterId;
        }
        return map;
    }

    public static bool IsStarPower(int cardId) => StarPowerSet.Contains(cardId);
    public static bool IsGadget(int cardId) => GadgetSet.Contains(cardId);

    /// <summary>Owning brawler for a card, or -1 when the layout does not tell us.</summary>
    public static int GetBrawlerForCard(int cardId) =>
        CardToBrawler.TryGetValue(cardId, out var brawler) ? brawler : -1;

    /// <summary>All star power + gadget card ids that exist at or below <paramref name="maxCardId"/>.</summary>
    public static IReadOnlyList<int> GetAbilityCards(int maxCardId)
    {
        return AllStarPowers.Concat(AllGadgets)
            .Where(id => id <= maxCardId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    public static int StarPowerCount(int maxCardId) => AllStarPowers.Count(id => id <= maxCardId);
    public static int GadgetCount(int maxCardId) => AllGadgets.Count(id => id <= maxCardId);
}
