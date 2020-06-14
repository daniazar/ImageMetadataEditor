using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Devices.Geolocation;
using Windows.Services.Maps;
using Windows.Graphics.Imaging;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.FileProperties;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace image_metadata_editor_winui3
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private IReadOnlyList<StorageFile> files;
        private int currentImageIndex = -1;
        readonly List<String> filesNames = new List<String>();
        String projectFolderName = "Project1";
        StorageFolder projectFolder;
        Geopoint myLocation;
        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void BtnOpenFileDialogClick(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");

            files = await picker.PickMultipleFilesAsync();
            if (files.Count > 0)
            {
                filesNames.Clear();
                // Application now has read/write access to the picked file(s)

                foreach (Windows.Storage.StorageFile file in files)
                {
                    filesNames.Add(file.Name);
                }

                comboImagesNames.DataContext = filesNames;
                comboImagesNames.UpdateLayout();
                comboImagesNames.SelectionChangedTrigger = ComboBoxSelectionChangedTrigger.Always;
                //comboImagesNames.SelectedIndex = 0;

            }
        }

        private async void BtnOpenFolderDialogClick(object sender, RoutedEventArgs e)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            Windows.Storage.StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                // Application now has read/write access to all contents in the picked folder
                // (including other sub-folder contents)
                Windows.Storage.AccessCache.StorageApplicationPermissions.
                FutureAccessList.AddOrReplace("PickedFolderToken", folder);
                files = await folder.GetFilesAsync();
                if (files.Count > 0)
                {
                    filesNames.Clear();
                    // Application now has read/write access to the picked file(s)

                    foreach (Windows.Storage.StorageFile file in files)
                    {
                        filesNames.Add(file.Name);
                    }
                    comboImagesNames.DataContext = filesNames;
                    comboImagesNames.UpdateLayout();
                    comboImagesNames.SelectionChangedTrigger = ComboBoxSelectionChangedTrigger.Always;
                    // comboImagesNames.SelectedItem = filesNames[0];
                }

            }
        }
        private void ComboImagesNamesSelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadImage(files.ElementAt(comboImagesNames.SelectedIndex));
            informationText.Text = "Working on image " + comboImagesNames.SelectedValue + " " + comboImagesNames.SelectedIndex + " / " + filesNames.Count;
        }
        private async void SaveSoftwareBitmapToFile(SoftwareBitmap softwareBitmap, StorageFile outputFile)
        {
            using (IRandomAccessStream stream = await outputFile.OpenAsync(FileAccessMode.ReadWrite))
            {
                // Create an encoder with the desired format
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);

                // Set the software bitmap
                encoder.SetSoftwareBitmap(softwareBitmap);

                // Set additional encoding parameters, if needed
                encoder.BitmapTransform.ScaledWidth = 320;
                encoder.BitmapTransform.ScaledHeight = 240;
                encoder.BitmapTransform.Rotation = Windows.Graphics.Imaging.BitmapRotation.Clockwise90Degrees;
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                encoder.IsThumbnailGenerated = true;

                try
                {
                    await encoder.FlushAsync();
                }
                catch (Exception err)
                {
                    const int WINCODEC_ERR_UNSUPPORTEDOPERATION = unchecked((int)0x88982F81);
                    switch (err.HResult)
                    {
                        case WINCODEC_ERR_UNSUPPORTEDOPERATION:
                            // If the encoder does not support writing a thumbnail, then try again
                            // but disable thumbnail generation.
                            encoder.IsThumbnailGenerated = false;
                            break;
                        default:
                            throw;
                    }
                }

                if (encoder.IsThumbnailGenerated == false)
                {
                    await encoder.FlushAsync();
                }


            }
        }

        private async Task CreateFolder()
        {

            StorageFolder rootFolder = await files.ElementAt(currentImageIndex).GetParentAsync();
            projectFolder = await rootFolder.CreateFolderAsync(projectFolderName, CreationCollisionOption.OpenIfExists);
        }

        private async Task WriteImage()
        {
            if( currentImageIndex == -1)
            {
                return;
            }
            try
            {

            await CreateFolder();
            StorageFile outputFile = await projectFolder.CreateFileAsync(filesNames.ElementAt(currentImageIndex), CreationCollisionOption.ReplaceExisting);
            var stream = (await outputFile.OpenStreamForWriteAsync()).AsRandomAccessStream();

            Guid encoderId;
            if (filesNames.ElementAt(currentImageIndex).Contains(".jpg"))
            {
                encoderId = Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId;
            }

            var inputStream = await files.ElementAt(currentImageIndex).OpenStreamForReadAsync();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(inputStream.AsRandomAccessStream());
            var inputProperties = decoder.BitmapProperties;
            var memStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            BitmapEncoder encoder2 = await BitmapEncoder.CreateForTranscodingAsync(memStream, decoder);

            WriteImageMetadata(encoder2, inputProperties);
            await encoder2.FlushAsync();
            memStream.Seek(0);
            stream.Seek(0);
            stream.Size = 0;
            await RandomAccessStream.CopyAsync(memStream, stream);

            memStream.Dispose();

            stream.Dispose();
            inputStream.Dispose();
            if (latitude.Text != null)
            {
                await GeotagHelper.SetGeotagAsync(outputFile, myLocation);
            }
            }
            catch( Exception err)
            {
                switch (err.HResult)
                {
                    case unchecked((int)0x88982F41): // WINCODEC_ERR_PROPERTYNOTSUPPORTED
                                                     // The file format does not support this property.
                        break;
                    default:
                        break;
                }
            }

        }

        private async void WriteImageMetadata(BitmapEncoder bitmapEncoder, BitmapPropertiesView inputProperties)
        {
            var propertySet = new Windows.Graphics.Imaging.BitmapPropertySet();

            var requests = new System.Collections.Generic.List<string>();
            requests.Add("System.Photo.Orientation");

            var retrievedProps = await inputProperties.GetPropertiesAsync(requests);
            if (retrievedProps.ContainsKey("System.Photo.Orientation"))
            {
                propertySet.Add("System.Photo.Orientation", retrievedProps["System.Photo.Orientation"]);
            }
            else
            {
                var orientationValue = new Windows.Graphics.Imaging.BitmapTypedValue(
                    1, // Defined as EXIF orientation = "normal"
                    Windows.Foundation.PropertyType.UInt16
                    );
                propertySet.Add("System.Photo.Orientation", orientationValue);

            }
            if (date.Date != null)
            {
                var datetaken = new Windows.Graphics.Imaging.BitmapTypedValue(
                    date.Date,
                    Windows.Foundation.PropertyType.DateTime
                    );
                propertySet.Add("System.Photo.DateTaken", datetaken);
            }
            var qualityValue = new Windows.Graphics.Imaging.BitmapTypedValue(
                1.0, // Maximum quality
                Windows.Foundation.PropertyType.Single
                );
            var location = new Windows.Graphics.Imaging.BitmapTypedValue(
                1.0, // Maximum quality
                Windows.Foundation.PropertyType.Single
                );
            propertySet.Add("ImageQuality", qualityValue);
            try
            {
                await bitmapEncoder.BitmapProperties.SetPropertiesAsync(propertySet);
            }
            catch (Exception err)
            {
                switch (err.HResult)
                {
                    case unchecked((int)0x88982F41): // WINCODEC_ERR_PROPERTYNOTSUPPORTED
                                                     // The file format does not support the requested metadata.
                        break;
                    case unchecked((int)0x88982F81): // WINCODEC_ERR_UNSUPPORTEDOPERATION
                                                     // The file format does not support any metadata.                        
                        break;
                }
            }
        }
        private async void LoadImage(StorageFile file)
        {
            if (file != null)
            {
                using (IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read))
                {
                    // Set the image source to the selected bitmap 
                    BitmapImage bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(fileStream);
                    image.Source = bitmapImage;
                    fileStream.Dispose();
                }
            }
        }

        private async void BtnSaveClick(object sender, RoutedEventArgs e)
        {
            await WriteImage();
        }
        private async void BtnSaveAndMoveClick(object sender, RoutedEventArgs e)
        {
            await WriteImage();
            if (currentImageIndex + 1 < filesNames.Count)
            {
                currentImageIndex++;
                comboImagesNames.SelectedIndex = currentImageIndex;
            }
        }

        private async void BtnSaveAllClick(object sender, RoutedEventArgs e)
        {
            do
            {
                await WriteImage();
                if (currentImageIndex + 1 < filesNames.Count)
                {
                    currentImageIndex++;
                    comboImagesNames.SelectedIndex = currentImageIndex;
                }
            } while (currentImageIndex < filesNames.Count);
        }


        private async void BtnUpdateGeoLocation(object sender, RoutedEventArgs e)
        {
            try
            {
                progress.IsActive = true;
                // The address or business to geocode.
                string addressToGeocode = address.Text + ", " + city.Text + ", " + state.Text + ", " + country.Text;
                //Update this key with your own from https://www.bingmapsportal.com/
                MapService.ServiceToken = "insert bing maps api";

                // The nearby location to use as a query hint.
                BasicGeoposition queryHint = new BasicGeoposition();
                queryHint.Latitude = 47.643;
                queryHint.Longitude = -122.131;
                Geopoint hintPoint = new Geopoint(queryHint);

                // Geocode the specified address, using the specified reference point
                // as a query hint. Return no more than 3 results.
                MapLocationFinderResult result =
                      await MapLocationFinder.FindLocationsAsync(
                                        addressToGeocode,
                                        hintPoint,
                                        3);
                // If the query returns results, display the coordinates
                // of the first result.
                if (result.Status == MapLocationFinderStatus.Success)
                {

                    latitude.Text = result.Locations[0].Point.Position.Latitude.ToString();
                    longitude.Text = result.Locations[0].Point.Position.Longitude.ToString();
                    progress.IsActive = false;
                    myLocation = result.Locations[0].Point;

                    // Set the map location.
                    /*map.Center = myLocation;
                    map.ZoomLevel = 12;
                    map.LandmarksVisible = true;*/
                }
            }catch(Exception ex)
            {
                progress.IsActive = false;

            }
        }

        private void CommandBarClose(object sender, object e)
        {
            //commandBar.IsOpen = true;
        }
    }
}
