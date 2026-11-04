using CommunityToolkit.Maui;
using Docket.Mobile.Services;
using Docket.Mobile.ViewModels;
using Docket.Mobile.Views;
using Plugin.LocalNotification;

namespace Docket.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseLocalNotification()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("selawk.ttf", "Selawik");
                fonts.AddFont("selawksb.ttf", "SelawikSemibold");
                fonts.AddFont("selawkb.ttf", "SelawikBold");
                fonts.AddFont("FluentSystemIcons-Regular.ttf", "FluentIcons");
                fonts.AddFont("FluentSystemIcons-Filled.ttf", "FluentIconsFilled");
            });

        // Fluent text fields: no Android underline; the surrounding FieldBox or card draws the edge.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Fluent", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetPadding(0, handler.PlatformView.PaddingTop, 0, handler.PlatformView.PaddingBottom);
#endif
        });
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("Fluent", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
#endif
        });

        builder.Services.AddSingleton<Session>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddTransient<SignInPage>();
        builder.Services.AddTransient<ListViewModel>();
        builder.Services.AddTransient<ListPage>();
        builder.Services.AddTransient<TaskViewModel>();
        builder.Services.AddTransient<TaskPage>();
        builder.Services.AddTransient<SettingsPage>();
        return builder.Build();
    }
}
