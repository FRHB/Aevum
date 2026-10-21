# Aevum Studio – Customer Portal (Blazor)

Requires the .NET 8 SDK.

    dotnet run

Then open https://localhost:7100/customer/portal

Structure
- Components/Pages/CustomerDashboard.razor  – main page (route /customer/portal)
- Components/Pages/BookingRow.razor         – reusable booking row
- Models/Booking.cs                         – Booking, GalleryAlbum, CustomerProfile
- wwwroot/app.css                           – theme variables
Sample data lives in the @code block of CustomerDashboard.razor; swap it for a service.
