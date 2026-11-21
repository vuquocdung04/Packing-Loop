using System;
using System.Collections.Generic;

/// <summary>
/// Dữ liệu 1 level (JSON, đọc bằng JsonUtility).
/// - road: tên asset RoadMapSO (vd "RoadMap_Network") → chọn RoadMapBuilder trong scene có map trùng tên.
/// - columns: nội dung từng cột trên road. (x, y) = 1 ô bất kỳ của cột theo toạ độ lưới map (x sang phải, y xuống, 0 = góc trên trái).
///   cars: từ xe ĐẦU (sát miệng ra của cột, ra trước) tới xe cuối. Màu = phần sau "CarColor-" của material. "" = ô trống.
/// - lot: các cột khu dưới (giữa 2 bãi cỏ), trái → phải; cars từ xe đầu (trên cùng) xuống.
/// </summary>
[Serializable]
public class LevelData
{
    public string road;
    public List<ColumnData> columns = new();
    public List<LotColumnData> lot = new();

    [Serializable]
    public class ColumnData
    {
        public int x, y;
        public List<string> cars = new();
        /// <summary>Cột bị chặn (obstacle): barrier ở 2 miệng, ô có nón / dấu X — không nhận, không cho xe ra.</summary>
        public bool blocked;
    }

    [Serializable]
    public class LotColumnData
    {
        public List<string> cars = new();
    }
}
