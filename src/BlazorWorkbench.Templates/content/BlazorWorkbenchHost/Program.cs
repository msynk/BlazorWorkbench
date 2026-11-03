using BlazorWorkbench.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register services your components need here, before AddBlazorWorkbench.
builder.AddBlazorWorkbench(options =>
{
    options.Title = "Company.Workbench";
    options.AddAssemblyOf<Company.Workbench.Components.HelloBadge>();
    // options.AddAssemblyOf<MyComponents.Button>();                      // your component library
    // options.PreviewStylesheets.Add("_content/MyComponents/theme.css"); // global CSS your components need
});

var app = builder.Build();
app.MapBlazorWorkbench();
app.Run();
