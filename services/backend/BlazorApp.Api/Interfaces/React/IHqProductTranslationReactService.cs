namespace BlazorApp.Api.Interfaces.React
{
    public interface IHqProductTranslationReactService
    {
        Task<TranslationResultDto> TranslateNamesByContainersAsync(List<string> containerGuids, bool overwriteExisting = false);
        Task<TranslationResultDto> TranslateNamesAllAsync(bool overwriteExisting = false);
    }

    public class TranslationResultDto
    {
        public int TotalCandidates { get; set; }
        public int TotalTranslated { get; set; }
        public int TotalSkipped { get; set; }
        public int TotalFailed { get; set; }
        public Dictionary<string, string> Samples { get; set; } = new();
    }
}
