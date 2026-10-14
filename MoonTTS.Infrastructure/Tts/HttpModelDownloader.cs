using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.Tts;

/* File Structure:
        - HttpModelDownloader(HttpClient httpClient)
        - DownloadModelAsync(
            ModelMetadata model, 
            string destinationFolder, 
            IProgress<double>? progress, 
            CancellationToken cancellationToken) : Task<Result>
        - CleanUpTempFile(string? tempPath) : void 
*/

public class HttpModelDownloader : IModelDownloader
{
    private readonly HttpClient _httpClient;
    
    /* Injects HttpClient to not open too many network sockets */
    public HttpModelDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Result> DownloadModelAsync(
        ModelMetadata model,
        string destinationFolder,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        string? currentTempFilePath = null;

        try
        {
            Directory.CreateDirectory(destinationFolder);

            long totalExpectedBytes = 0;

            /* Requests the file size to be downloaded */
            foreach (var url in model.DownloadFiles.Values)
            {
                using var headRequest = new HttpRequestMessage(HttpMethod.Get, url);
                using var headResponse = await _httpClient.SendAsync(
                    headRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (headResponse.Content.Headers.ContentLength.HasValue)
                {
                    totalExpectedBytes += headResponse.Content.Headers.ContentLength.Value;
                }
            }

            long totalDownloaded = 0;

            foreach (var file in model.DownloadFiles)
            {
                string expectedFileName = file.Key;
                string remoteUrl = file.Value;

                /* Calculates file paths */
                string finalFilePath = Path.Combine(destinationFolder, expectedFileName);
                currentTempFilePath = finalFilePath + ".tmp";

                /* Opens HTTP connection */
                using var response = await _httpClient.GetAsync(
                    remoteUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure(
                        $"Server returned error {(int)response.StatusCode}: {response.ReasonPhrase} for {expectedFileName}.");
                }

                /* Streams the file from HF to the storage */
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(
                    currentTempFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    8192,
                    true);

                byte[] buffer = new byte[8192]; /* 8KB chunks */
                int bytesRead;

                /* Loop until the download finishes, or the user clicks "Cancel" */
                while ((bytesRead = await contentStream.ReadAsync(
                           buffer,
                           0,
                           buffer.Length,
                           cancellationToken)) != 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalDownloaded += bytesRead;

                    /* Reports progress back to the UI */
                    if (progress != null && totalExpectedBytes > 0)
                    {
                        double percentage = Math.Round((double)totalDownloaded / totalExpectedBytes * 100, 2);
                        progress.Report(percentage);
                    }
                }

                /* Ensures data is saved before renaming */
                await fileStream.FlushAsync(cancellationToken);
                fileStream.Close();

                /* Renames the .tmp to the correct name */
                if (File.Exists(finalFilePath))
                    File.Delete(finalFilePath);
                File.Move(currentTempFilePath, finalFilePath);
                currentTempFilePath = null;
            }

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            CleanUpTempFile(currentTempFilePath);
            return Result.Failure("Model download was canceled.");
        }
        catch (HttpRequestException ex)
        {
            CleanUpTempFile(currentTempFilePath);
            return Result.Failure($"Network error while downloading model: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            CleanUpTempFile(currentTempFilePath);
            return Result.Failure($"Failed to download model files: {ex.Message}", ex);
        }
    }

    private static void CleanUpTempFile(string? tempPath)
    {
        if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
        {
            try { File.Delete(tempPath); } catch { /* Ignores cleanup errors */ }
        }
    }
}