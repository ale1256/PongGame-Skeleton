namespace TheAdventure;

//pentru încărcare/salvare ca jocul să nu depindă direct de fișiere json
public interface IStorage<T>
{
    Task<T?> TryLoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(T value, CancellationToken cancellationToken = default);
}
