namespace GardenHelper.App.Infrastructure;

public static partial class Strings
{
    // key -> (English, Romanian). Add both texts together so the two languages never drift apart.
    private static readonly (string Key, string En, string Ro)[] Misc = new[]
    {
        ("confirm_delete", "Delete this item? This cannot be undone.", "Ștergi acest element? Acțiunea nu poate fi anulată."),
        ("wild_mushroom_rule", "Never eat a wild mushroom because of what an app or book says. Have every mushroom checked in person by an authorised mushroom expert before eating it. If someone has eaten a mushroom you are unsure of, call 112 at once and keep a sample; the worst poisonings show symptoms only after many hours.", "Nu mânca niciodată o ciupercă sălbatică pe baza a ce spune o aplicație sau o carte. Pune fiecare ciupercă să fie verificată personal de un specialist autorizat înainte de a o mânca. Dacă cineva a mâncat o ciupercă despre care nu ești sigur, sună imediat la 112 și păstrează o probă; cele mai grave otrăviri dau simptome abia după multe ore."),
    };
}
