namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Vai trò trong kíp / tổ làm việc
/// </summary>
public enum CrewRole
{
    /// <summary>
    /// Tổ trưởng / Chỉ huy kíp (tối đa 1 người mỗi tổ)
    /// </summary>
    Commander = 0,
    Driver = 1,
    Firefighter = 2,
    Rescuer = 3,
    Medic = 4
}
