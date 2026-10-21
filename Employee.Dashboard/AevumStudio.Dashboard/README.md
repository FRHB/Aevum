# Aevum Studio – Employee Dashboard (Blazor Web App, .NET 8)

## Run
```
dotnet run
```
Then open http://localhost:5080/employee/dashboard

In Visual Studio: open `AevumStudio.Dashboard.csproj` (or File > Open > Project) and press F5.

## Structure
- `Program.cs` – app startup (interactive server rendering)
- `Components/App.razor` – HTML shell, loads CSS and blazor.web.js
- `Components/Routes.razor` – router
- `Components/Layout/MainLayout.razor` – bare layout
- `Components/Pages/EmployeeDashboard.razor` – markup (routes `/` and `/employee/dashboard`)
- `Components/Pages/EmployeeDashboard.razor.cs` – code-behind
- `wwwroot/css/dashboard.css` – dashboard styles (light/dark)
