using System;
using System.Collections.Generic;
using System.Text;
using MediaBrowser.LibraryManagement;
using System.IO;
using MediaBrowser.Library.Providers.Attributes;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Configuration;

namespace MediaBrowser.Library.Providers
{
    [ProviderPriority(20)]
    [SupportedType(typeof(BaseItem))]
    class ImageByNameProvider : ImageFromMediaLocationProvider
    {
        protected string location;
        protected override string Location
        {
            get {
                if (location == null)
                {

                    location = ApplicationPaths.AppIBNPath;

                    //sub-folder is based on the type of thing we're looking for
                    if (Item is Genre)
                            location = Path.Combine(location, "Genre");
                    else if (Item is Person)
                            location = Path.Combine(location, "People");
                    else if (Item is Studio)
                            location = Path.Combine(location, "Studio");
                    else if (Item is Year)
                            location = Path.Combine(location, "Year");
                    else
                            location = Path.Combine(location, "General");


                    char[] invalid = Path.GetInvalidFileNameChars();

                    string name = Item.Name;
                    foreach (char c in invalid)
                        name = name.Replace(c.ToString(), "");
                    location = Path.Combine(location, name);

                    if (!Directory.Exists(location))
                    {
                        //try the default area by type
                        location = ApplicationPaths.AppIBNPath; //reset to root
                        location = Path.Combine(location, "Default\\"+Item.GetType().Name);

                        //Logging.Logger.ReportVerbose("IBN provider looking for: " + location);

                        //now we have a specific default folder for this type - be sure it exists
                        if (!Directory.Exists(location))
                        {
                            //nope - use a generic
                            string baseType = Item is Folder ? "folder" : "video";
                            location = Path.Combine(ApplicationPaths.AppIBNPath, "default\\" + baseType);
                            //Logging.Logger.ReportVerbose("IBN provider defaulting to: " + location);
                        }
                    }
                }
                return location;
            }
        }

        
    }
}
