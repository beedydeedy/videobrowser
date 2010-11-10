using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaBrowser.Library.Persistance;
using MediaBrowser.Library.Entities.Attributes;
using MediaBrowser.Library.Extensions;

namespace MediaBrowser.Library.Entities {
    public class Studio : BaseItem {
          public static Guid GetStudioId(string name) {
            return ("studio" + name.Trim()).GetMD5();
        }

        public static Studio GetStudio(string name) {
            Guid id = GetStudioId(name);
            var studio = Kernel.Instance.ItemRepository.RetrieveItem(id) as Studio;
            if (studio == null || studio.Name == null) {
                studio = new Studio(id, name.Trim());
                Kernel.Instance.ItemRepository.SaveItem(studio);
            }
            return studio;
        }

        public Studio() {
        }

        [Persist]
        [NotSourcedFromProvider]
        string name;

        public override string Name {
            get {
                return name;
            }
            set {
                name = value;
            }
        }

        public Studio(Guid id, string name) {
            this.name = name;
            this.Id = id;
        }
    }
}
