using Microsoft.AspNetCore.Http;
using NzbWebDAV.Api.SabControllers;
using NzbWebDAV.Api.SabControllers.GetCategories;
using NzbWebDAV.Api.SabControllers.GetQueue;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.Api;

public class SabCategoryTests
{
    [Fact]
    public void CategoryResolver_AcceptsCategoryAlias()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?category=movies");

        Assert.Equal("movies", SabCategoryResolver.GetCategory(context, new ConfigManager()));
    }

    [Fact]
    public void CategoryResolver_PrefersCatWhenBothAliasesArePresent()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?cat=tv&category=movies");

        Assert.Equal("tv", SabCategoryResolver.GetCategory(context, new ConfigManager()));
    }

    [Fact]
    public void CategoryResolver_MapsStarToManualCategory()
    {
        var config = CreateConfig();
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?cat=*");

        var request = new GetQueueRequest(context, config);

        Assert.Equal("manual", request.Category);
    }

    [Fact]
    public void GetCategories_PrependsStarWithoutAddingAWebDavCategory()
    {
        var config = CreateConfig();

        var apiCategories = GetCategoriesController.BuildCategories(config);

        Assert.Equal(["*", "manual", "movies", "tv"], apiCategories);
        Assert.Equal(["manual", "movies", "tv"], config.GetApiCategories());
    }

    private static ConfigManager CreateConfig()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.ApiManualCategory,
                ConfigValue = "manual",
            },
            new ConfigItem
            {
                ConfigName = ConfigKeys.ApiCategories,
                ConfigValue = "movies,tv",
            },
        ]);
        return config;
    }
}
