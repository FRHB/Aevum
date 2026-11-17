using System;
using System.Data;
using PhotoAevumStudio.Web.Services;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class UserServiceImageMappingTests
{
    [Fact]
    public void MapEquipmentRow_WhenEquipmentImageIsByteArrayAndImagePathIsNull_DoesNotThrowInvalidCastException()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        using var table = CreateEquipmentTable(includeImagePath: true);
        table.Rows.Add("EQP-0001", "Sony A7 IV", "ET-CAM", "Camera", "SN-123", "Available", DBNull.Value, bytes);

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var dto = UserService.MapEquipmentRow(reader);

        Assert.Equal("EQP-0001", dto.Id);
        Assert.Equal("Sony A7 IV", dto.Name);
        Assert.Equal($"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}", dto.Image);
    }

    [Fact]
    public void MapEquipmentRow_WhenImagePathIsPresent_PrefersImagePath()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03 };
        using var table = CreateEquipmentTable(includeImagePath: true);
        table.Rows.Add("EQP-0002", "Godox AD600", "ET-LGT", "Lighting", "SN-456", "Available", "equipment/ad600.jpg", bytes);

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var dto = UserService.MapEquipmentRow(reader);

        Assert.Equal("equipment/ad600.jpg", dto.Image);
    }

    [Fact]
    public void MapEquipmentRow_WhenLegacySevenColumnsHasByteArrayAtOrdinal6_DoesNotThrow()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        using var table = CreateEquipmentTable(includeImagePath: false);
        table.Rows.Add("EQP-0003", "DJI RS3", "ET-GMB", "Gimbal", "SN-789", "Maintenance", bytes);

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var dto = UserService.MapEquipmentRow(reader);

        Assert.Equal($"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}", dto.Image);
    }

    [Theory]
    [InlineData("ServiceImage", "ServiceImageMimeType", false)]
    [InlineData("ComboPackageImage", "ComboPackageImageMimeType", false)]
    [InlineData("AdditionalServiceImage", "AdditionalServiceImageMimeType", true)]
    public void ReadServiceImageAndMime_WithStoredProcedureColumns_ReadsBytesAndMimeWithoutCastError(
        string imageCol,
        string mimeCol,
        bool includeCategoryColumn)
    {
        var bytes = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Description", typeof(string));
        table.Columns.Add("Price", typeof(decimal));
        if (includeCategoryColumn)
        {
            table.Columns.Add("CategoryName", typeof(string));
        }
        table.Columns.Add(imageCol, typeof(byte[]));
        table.Columns.Add(mimeCol, typeof(string));

        if (includeCategoryColumn)
        {
            table.Rows.Add("ADD-0001", "Extra Print", "Desc", 500m, "Add-On", bytes, "image/png");
        }
        else
        {
            table.Rows.Add("SVC-0001", "Portrait", "Desc", 2500m, bytes, "image/png");
        }

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var (image, mimeType) = UserService.ReadServiceImageAndMime(reader, imageCol, mimeCol);

        Assert.Equal(bytes, image);
        Assert.Equal("image/png", mimeType);
    }

    [Fact]
    public void ReadServiceImageAndMime_WithTwoColumnFallback_ReadsBytesAndMime()
    {
        var bytes = new byte[] { 0xAB, 0xCD };
        using var table = new DataTable();
        table.Columns.Add("ServiceImage", typeof(byte[]));
        table.Columns.Add("ServiceImageMimeType", typeof(string));
        table.Rows.Add(bytes, "image/webp");

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var (image, mimeType) = UserService.ReadServiceImageAndMime(reader, "ServiceImage", "ServiceImageMimeType");

        Assert.Equal(bytes, image);
        Assert.Equal("image/webp", mimeType);
    }

    private static DataTable CreateEquipmentTable(bool includeImagePath)
    {
        var table = new DataTable();
        table.Columns.Add("EquipmentID", typeof(string));
        table.Columns.Add("EquipmentName", typeof(string));
        table.Columns.Add("EquipmentTypeID", typeof(string));
        table.Columns.Add("TypeName", typeof(string));
        table.Columns.Add("SerialNo", typeof(string));
        table.Columns.Add("EquipmentStatus", typeof(string));
        if (includeImagePath)
        {
            table.Columns.Add("ImagePath", typeof(string));
        }
        table.Columns.Add("EquipmentImage", typeof(byte[]));
        return table;
    }
}
