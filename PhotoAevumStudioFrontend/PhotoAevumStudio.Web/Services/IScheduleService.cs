using System;
using System.Collections.Generic;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public interface IScheduleService
{
    // Studio Spaces & Events
    List<StudioSpaceResource> GetStudioSpaces();
    List<EventBookingModel> GetEventBookings(DateTime? startDate = null, DateTime? endDate = null);
    void AddEventBooking(EventBookingModel booking);
    void UpdateEventBooking(EventBookingModel booking);
    void DeleteEventBooking(string id);
    int GetEventConflictCount(DateTime? startDate = null, DateTime? endDate = null);

    // Staff Resources & Shifts
    List<StaffMemberResource> GetStaffMembers();
    List<StaffShiftModel> GetStaffShifts();
    void AddStaffShift(StaffShiftModel shift);
    void UpdateStaffShift(StaffShiftModel shift);
    void DeleteStaffShift(string id);
    int GetShiftConflictCount();

    // Cache control
    void InvalidateCache();

    // Equipment Resources & Reservations
    List<EquipmentItemResource> GetEquipmentResources();
    List<EquipmentReservationModel> GetEquipmentReservations();
    void AddEquipmentReservation(EquipmentReservationModel reservation);
    void UpdateEquipmentReservation(EquipmentReservationModel reservation);
    void DeleteEquipmentReservation(string id);
    int GetEquipmentConflictCount();
}
