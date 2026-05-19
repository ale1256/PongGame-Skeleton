using System.Text.Json;

namespace TheAdventure;

//storage generic pe fișier JSON 
public sealed class JsonFileStorage<T> : IStorage<T>, IDisposable
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileStorage(string filePath, JsonSerializerOptions? options = null)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _options = options ?? new JsonSerializerOptions { WriteIndented = true };
    }

    public async Task<T?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        // Lock async
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return default;
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveDataException($"Failed to read save file at '{_filePath}'.", ex);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, _options);
            }
            catch (JsonException ex)
            {
                throw new SaveDataException($"Save file at '{_filePath}' is not valid JSON.", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        // evită citire/scriere simultană.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? directory;
            try
            {
                directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveDataException($"Failed to create directory for '{_filePath}'.", ex);
            }

            string json;
            try
            {
                json = JsonSerializer.Serialize(value, _options);
            }
            catch (Exception ex)
            {
                throw new SaveDataException($"Failed to serialize save data for '{_filePath}'.", ex);
            }

            // scriem într-un temp file și apoi facem move peste original.
            var tempPath = _filePath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveDataException($"Failed to write save file at '{_filePath}'.", ex);
            }
            finally
            {
                // Best effort cleanup.
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                   
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
