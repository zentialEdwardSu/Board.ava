using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace saint.Board.ava.utils;

public class ImageLoader
{
    
    public WindowNotificationManager? NotificationManager { get; set; }
    
    public async Task<Bitmap?> LoadImageFromFolder(IStorageFile file)
    {
        try
        {
            using (var stream = file.OpenReadAsync())
            {
                return new Bitmap(await stream);
            }
        }
        catch (FileNotFoundException ex)
        {
            NotificationManager?.Show(new Notification("Failed to Load Image",$"File Not found: {ex.Message}",NotificationType.Error));
        }
        catch (NotSupportedException ex)
        {
            NotificationManager?.Show(new Notification("Failed to Load Image",$"Unsupported image format: {ex.Message}",NotificationType.Error));
        }
        catch (Exception ex)
        {
            NotificationManager?.Show(new Notification("Failed to Load Image",$"Failed to load Image: {ex.Message}",NotificationType.Error));
        }

        return null;
    }
}