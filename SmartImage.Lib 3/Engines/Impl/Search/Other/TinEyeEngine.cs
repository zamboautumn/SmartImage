﻿namespace SmartImage.Lib.Engines.Impl.Search.Other;

public sealed class TinEyeEngine : BaseSearchEngine
{
    public TinEyeEngine() : base("https://www.tineye.com/search?url=") { }

    public override SearchEngineOptions EngineOption => SearchEngineOptions.TinEye;

    public override void Dispose()
    {
    }
}