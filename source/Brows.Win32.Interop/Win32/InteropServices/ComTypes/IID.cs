namespace Brows.Win32.InteropServices.ComTypes;

internal static class IID {
    public const string IExtractImage = "BB2E617C-0920-11d1-9A0B-00C04FC2D6C1";
    public const string IFileOperation = "947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8";
    public const string IFileOperationProgressSink = "04b0f1a7-9490-44bc-96e1-4296a31252e2";
    public const string IOperationsProgressDialog = "0C9FB851-E5C9-43EB-A370-F0677B13874C";
    public const string ISharedBitmap = "091162a4-bc96-411f-aae8-c5122cd03363";
    public const string IShell = "286E6F1B-7113-4355-9562-96B7E9D64C54";
    public const string IShellItem = "43826D1E-E718-42EE-BC55-A1E261C37BFE";
    public const string IShellItemImageFactory = "bcc18b79-ba16-442f-80c4-8a59c30c463b";
    public const string IThumbnailCache = "F676C15D-596A-4ce2-8234-33996F445DB1";
    public const string IShellIconOverlayIdentifier = "0c6c4200-c589-11d0-999a-00c04fd655e1";
    public const string IObjectWithSite = "fc4801a3-2ba9-11cf-a229-00aa003d7352";
    public const string IQueryAssociations = "c46ca590-3c3f-11d2-bee6-0000f805ca57";

    public static class Managed {
        public static readonly Guid IShellItem = new Guid(IID.IShellItem);
        public static readonly Guid IQueryAssociations = new Guid(IID.IQueryAssociations);
    }
}
