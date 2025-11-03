namespace API.DataModels.LDAP
{
    /// <summary>
    /// 用於更新 Active Directory 使用者資訊的資料模型。
    /// 僅包含允許透過 API 修改的欄位。
    /// </summary>
    public class AdUserUpdateModel
    {
        /// <summary>
        /// 使用者描述 (對應 AD 的 description 屬性)。
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 辦公室位置 (對應 AD 的 physicalDeliveryOfficeName 屬性)。
        /// </summary>
        /// <remarks>
        /// 模型中使用 Office 作為屬性名以提高可讀性。
        /// </remarks>
        public string Office { get; set; }

        /// <summary>
        /// 員工編號 (對應 AD 的 streetAddress 屬性)。
        /// </summary>
        /// <remarks>
        /// 模型中使用 EmployeeId 作為屬性名以提高可讀性。
        /// </remarks>
        public string EmployeeId { get; set; }

        /// <summary>
        /// 部門 (對應 AD 的 department 屬性)。
        /// </summary>
        public string Department { get; set; }

        /// <summary>
        /// 職稱 (對應 AD 的 title 屬性)。
        /// </summary>
        public string Title { get; set; }
    }
}