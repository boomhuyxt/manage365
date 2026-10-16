using System.Globalization;
using System.Text.RegularExpressions;

namespace manage365.Routes.API.Shifts;

public static class ShiftInput
{
    public static DateOnly Date(string? value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date.Year is < 1900 or > 9998)
            throw new ArgumentException("Ngày phải có định dạng yyyy-MM-dd và nằm trong khoảng 1900–9998.");
        return date;
    }

    public static TimeOnly Time(string? value)
    {
        if (!TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException("Giờ phải có định dạng HH:mm (00:00–23:59).");
        return time;
    }

    public static void Interval(TimeOnly start, TimeOnly end, bool overnight)
    {
        if (start == end || (overnight ? end > start : end < start))
            throw new ArgumentException("Ca phải dài hơn 0 và ngắn hơn 24 giờ; bật qua đêm khi giờ kết thúc nhỏ hơn giờ bắt đầu.");
    }

    public static void Text(string? value, string name, int max, bool required = false)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > max)
            throw new ArgumentException($"{name} {(required ? "không được trống và " : "")}không vượt quá {max} ký tự.");
    }

    public static void Template(string? code, string? name, string? start, string? end, bool overnight, string? color, string? description)
    {
        if (code is not null) Text(code, "Mã ca", 50, true);
        Text(name, "Tên ca", 150, true);
        Text(description, "Mô tả", 2000);
        if (color is not null && !Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
            throw new ArgumentException("Màu ca phải có dạng #RRGGBB.");
        Interval(Time(start), Time(end), overnight);
    }

    public static void Assignment(AssignShiftRequest request)
    {
        if (request.EmployeeId <= 0 || request.ShiftTemplateId is <= 0)
            throw new ArgumentException("ID nhân viên/mẫu ca phải là số dương.");
        Date(request.ShiftDate);
        Text(request.Note, "Ghi chú", 2000);
        Text(request.CustomShiftName, "Tên ca", 150);
        if (request.ShiftTemplateId.HasValue)
        {
            if (request.CustomStartTime is not null || request.CustomEndTime is not null)
                throw new ArgumentException("Chọn mẫu ca hoặc giờ tùy chỉnh, không chọn cả hai.");
        }
        else
        {
            var start = Time(request.CustomStartTime);
            var end = Time(request.CustomEndTime);
            Interval(start, end, request.IsOvernight ?? end < start);
        }
    }

    public static void Update(UpdateShiftAssignmentRequest request)
    {
        if (request.ShiftTemplateId is <= 0) throw new ArgumentException("ID mẫu ca phải là số dương.");
        Text(request.ShiftName, "Tên ca", 150);
        Text(request.Note, "Ghi chú", 2000);
        if (request.Status is not null && request.Status is not ("Approved" or "Cancelled"))
            throw new ArgumentException("Trạng thái phân ca phải là Approved hoặc Cancelled.");
        if (request.ShiftTemplateId.HasValue && (request.StartTime is not null || request.EndTime is not null))
            throw new ArgumentException("Chọn mẫu ca hoặc giờ tùy chỉnh, không chọn cả hai.");
        if (request.StartTime is not null) Time(request.StartTime);
        if (request.EndTime is not null) Time(request.EndTime);
    }
}
