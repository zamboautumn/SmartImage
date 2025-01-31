﻿namespace SmartImage.Lib.Engines.Impl.Search.Other;

public sealed class GoogleImagesEngine : BaseSearchEngine
{
    public GoogleImagesEngine() : base("https://lens.google.com/uploadbyurl?url=") { }

    public override string Name => "Google Images";

    public override SearchEngineOptions EngineOption => SearchEngineOptions.GoogleImages;

    #region Overrides of BaseSearchEngine

    public override void Dispose() { }

    #endregion

    // https://html-agility-pack.net/knowledge-base/2113924/how-can-i-use-html-agility-pack-to-retrieve-all-the-images-from-a-website-
}