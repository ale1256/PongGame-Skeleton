namespace TheAdventure;

// date persistente 
public sealed record SaveData(
    int BestRally, //cel mai bun rally all-time 
    int GamesPlayed, //cate meciuri s-au terminat.
    DateTimeOffset LastPlayedUtc //ultima rulare a jocului 
)
{
//valori default dacă nu există fișierul de save
    public static SaveData Default => new(BestRally: 0, GamesPlayed: 0, LastPlayedUtc: DateTimeOffset.MinValue);
}
