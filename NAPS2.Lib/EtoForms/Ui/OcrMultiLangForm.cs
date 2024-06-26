using Eto.Drawing;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Ocr;

namespace NAPS2.EtoForms.Ui;

public class OcrMultiLangForm : EtoDialogBase
{
    private readonly IListView<Language> _languageList;

    public OcrMultiLangForm(Naps2Config config, TesseractLanguageManager tesseractLanguageManager,
        OcrLanguagesListViewBehavior ocrLanguagesListViewBehavior) : base(config)
    {
        _languageList = EtoPlatform.Current.CreateListView(ocrLanguagesListViewBehavior);
        _languageList.SetItems(tesseractLanguageManager.InstalledLanguages.OrderBy(x => x.Name));
    }

    public string? Code { get; private set; }

    protected override void BuildLayout()
    {
        Title = UiStrings.OcrMultiLangFormTitle;
        Icon = new Icon(1f, Icons.text_small.ToEtoImage());

        FormStateController.RestoreFormState = false;
        FormStateController.DefaultExtraLayoutSize = new Size(150, 20);

        LayoutController.Content = L.Column(
            _languageList.Control.Scale(),
            C.Spacer(),
            L.Row(
                C.Filler(),
                L.OkCancel(
                    C.OkButton(this, Save),
                    C.CancelButton(this))
            )
        );
    }

    private void Save()
    {
        if (_languageList.Selection.Count > 0)
        {
            Code = string.Join("+", _languageList.Selection.OrderBy(x => x.Name).Select(lang => lang.Code));
        }
    }
}