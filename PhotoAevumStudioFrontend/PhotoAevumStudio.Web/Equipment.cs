using Microsoft.AspNetCore.Components;
using System.Collections.Generic;

namespace PhotoAevumStudio.Web.Components.Pages
{
    public class EquipmentBase : ComponentBase
    {
        protected List<EquipmentItem> EquipmentList { get; set; } = new()
        {
            new EquipmentItem { Id = 2084, SerialNumber = "SN-99321", Name = "Godox AD600Pro", Kind = "Camera & Lighting Gear", Status = "Available" },
            new EquipmentItem { Id = 2085, SerialNumber = "SN-44102", Name = "C-Stand Heavy", Kind = "Studio Support", Status = "In Use" },
            new EquipmentItem { Id = 2086, SerialNumber = "SN-11883", Name = "Sony FX3 Body", Kind = "Camera & Lighting Gear", Status = "Maintenance" }
        };

        protected void OpenAddEquipmentModal()
        {
            // Logic to trigger add equipment wizard steps 1-3
        }
    }

    public class EquipmentItem
    {
        public int Id { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Status { get; set; } = "Available";
    }
}
