using System.Text.Json.Nodes;
namespace MEdufine;

/// <summary>
/// Deterministic synthetic data used only by --ui-demo for Windows GUI/keyboard
/// testing. It never contacts a browser, a server or an institution. Every screen
/// that renders it is labelled as synthetic.
/// </summary>
internal static class DemoData
{
    static JsonObject Menu(string path0, params string[] path)
    {
        var full = new[] { path0 }.Concat(path).ToArray();
        return new JsonObject
        {
            ["id"] = "m" + string.Join("_", full),
            ["label"] = full[^1],
            ["level"] = full.Length - 1,
            ["path"] = new JsonArray(full.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["hasChildren"] = false,
            ["active"] = false,
            ["expanded"] = false,
            ["region"] = "업무 메뉴",
            ["kind"] = "메뉴"
        };
    }

    static JsonObject Control(string id, string kind, string label, string value, bool editable = false, bool webOnly = false, string context = "")
        => new()
        {
            ["id"] = id, ["kind"] = kind, ["label"] = label, ["value"] = value,
            ["editValue"] = editable ? value : "", ["editable"] = editable,
            ["disabled"] = false, ["webOnly"] = webOnly, ["context"] = context, ["options"] = new JsonArray()
        };

    public static JsonObject Scan()
    {
        var menus = new JsonArray(
            Menu("업무관리"),
            Menu("업무관리", "계약관리"),
            Menu("업무관리", "계약관리", "계약요청"),
            Menu("업무관리", "계약관리", "계약검토"),
            Menu("업무관리", "물품관리"),
            Menu("업무관리", "물품관리", "물품등록"),
            Menu("업무관리", "물품관리", "물품조회"),
            Menu("문서관리"),
            Menu("문서관리", "문서함"),
            Menu("문서관리", "공람대기"),
            Menu("문서관리", "결재대기"),
            Menu("기능분류"),
            Menu("기능분류", "예산관리"),
            Menu("기능분류", "예산관리", "예산편성"),
            Menu("기능분류", "예산관리", "예산집행")
        );

        var controls = new JsonArray(
            Control("1", "입력", "물품명", "업무용 모니터", editable: true, context: "조회 조건"),
            Control("2", "입력", "관리번호", "SYN-0001", editable: true, context: "조회 조건"),
            Control("3", "선택", "사용 부서", "총무과", context: "조회 조건"),
            Control("4", "버튼", "조회", "", context: "조회"),
            Control("5", "버튼", "저장", "", context: "저장"),
            Control("6", "버튼", "지급 승인", "", webOnly: true, context: "중요 처리")
        );

        var tables = new JsonArray(new JsonObject
        {
            ["name"] = "물품 조회 결과(합성)",
            ["columns"] = new JsonArray("물품명", "수량", "상태"),
            ["rows"] = new JsonArray(
                new JsonArray("물품명", "수량", "상태"),
                new JsonArray("모니터", "3", "정상"),
                new JsonArray("책상", "5", "정상"),
                new JsonArray("의자", "12", "점검"))
        });

        var page = new JsonObject
        {
            ["frameId"] = 0, ["docId"] = "demo-doc", ["revision"] = "demo-rev",
            ["host"] = "demo.invalid", ["title"] = "합성 업무 화면 · 실제 K-에듀파인 아님",
            ["menus"] = menus, ["controls"] = controls, ["tables"] = tables,
            ["protectedCount"] = 0, ["truncated"] = false,
            ["framePath"] = new JsonArray(new JsonObject { ["frameId"] = 0, ["host"] = "demo.invalid", ["path"] = "/synthetic" })
        };

        return new JsonObject
        {
            ["ok"] = true, ["link"] = 0, ["host"] = "demo.invalid (합성)", ["urlPath"] = "/synthetic",
            ["title"] = "합성 업무 화면 · 실제 K-에듀파인 아님",
            ["pages"] = new JsonArray(page), ["inaccessible"] = 0, ["truncated"] = false
        };
    }
}
