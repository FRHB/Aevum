using Microsoft.AspNetCore.Components;
using System.Collections.Generic;

namespace PhotoAevumStudio.Web.Components.Pages
{
    public class ServicesBase : ComponentBase
    {
        protected List<ServiceItem> ServicesList { get; set; } = new()
        {
            new ServiceItem { Id = 1041, Name = "Deep Tissue Massage", Kind = "Service", Duration = 60, Price = 75.00m },
            new ServiceItem { Id = 1042, Name = "Studio Lighting Combo", Kind = "Combo Package", Duration = 120, Price = 150.00m },
            new ServiceItem { Id = 1043, Name = "Express Headshot Session", Kind = "Additional Service", Duration = 30, Price = 45.00m }
        };

        protected void OpenAddServiceModal()
        {
            // Logic to trigger modal wizard dialog steps 1-4
        }
    }

    public class ServiceItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int Duration { get; set; }
        public decimal Price { get; set; }
    }
}
