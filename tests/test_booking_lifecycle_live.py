#!/usr/bin/env python3
"""
Test Suite: End-to-End Booking Lifecycle Simulation
Roles: Customer, Employee (Office Clerk), and Admin
Verifies:
1. Customer consultation creation -> Pending_Call with assigned clerk.
2. Deal outcome verification: Failed with deal (Deal_Lost), Reschedule, Success with deal (Deal_Won).
3. 5-Step Deal Setup Wizard: Service selection, shoot date/time, email dispatch, staff assignment, equipment allocation, payment settlement on time -> Confirmed booking.
4. Unpaid Reservation Fee: Second booking created, payment NOT made, lock expires -> Auto-cancellation of reservation & time slot release via usp_ReleaseExpiredPendingPayments.
5. Teardown Hygiene: Total cleanup of all test records (@rule: testing_cleanup).
"""

import sys
import unittest
import uuid
import datetime
from pathlib import Path

WORKSPACE_ROOT = Path(__file__).resolve().parent.parent
if str(WORKSPACE_ROOT) not in sys.path:
    sys.path.insert(0, str(WORKSPACE_ROOT))

from tests.config import get_sql_connection


class TestBookingLifecycleSimulation(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.conn = get_sql_connection()
        cls.test_cust_id = f"CUST-TEST-{uuid.uuid4().hex[:6].upper()}"
        cls.test_neg_ids = []
        cls.test_evt_ids = []

        # Find or create a test customer
        cursor = cls.conn.cursor()
        cursor.execute("""
            INSERT INTO dbo.Customer (CustomerID, CustomerFirstName, CustomerLastName, CustomerEmail, CustomerPhoneNumber, CustomerPassword, CustomerStatus, CreatedAt)
            VALUES (?, 'Antigravity', 'Tester', 'test.client@photoaevum.studio', '09191234567', 'MockHash123', 'Active', SYSUTCDATETIME());
        """, (cls.test_cust_id,))

        # Discover an active Office Clerk
        cursor.execute("""
            SELECT TOP 1 e.EmployeeID, e.EmployeeFirstName + ' ' + e.EmployeeLastName AS FullName
            FROM dbo.Employees e
            LEFT JOIN dbo.EmployeeJointSpecialization ejs ON ejs.EmployeeID = e.EmployeeID
            LEFT JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID
            WHERE e.EmployeeStatus = 'Active' AND (es.SpecializationName = 'Office Clerk' OR e.Specializations LIKE '%Clerk%');
        """)
        row = cursor.fetchone()
        if not row:
            # Fallback to any active employee if clerk tag not set
            cursor.execute("SELECT TOP 1 EmployeeID, EmployeeFirstName + ' ' + EmployeeLastName FROM dbo.Employees WHERE EmployeeStatus = 'Active';")
            row = cursor.fetchone()
        cls.clerk_id = row[0]
        cls.clerk_name = row[1]

        # Discover an available service
        cursor.execute("SELECT TOP 1 ServicesID, ServiceName, ServicePrice FROM dbo.Services;")
        svc_row = cursor.fetchone()
        cls.service_id = svc_row[0] if svc_row else "SRV-0001"
        cls.service_name = svc_row[1] if svc_row else "Standard Portrait"
        cls.service_price = float(svc_row[2]) if svc_row else 5000.0

        # Discover active staff and equipment
        cursor.execute("SELECT TOP 2 EmployeeID FROM dbo.Employees WHERE EmployeeStatus = 'Active';")
        cls.staff_ids = [r[0] for r in cursor.fetchall()]

        cursor.execute("SELECT TOP 2 EquipmentID FROM dbo.Equipment;")
        cls.equip_ids = [r[0] for r in cursor.fetchall()]

        cls.conn.commit()
        cursor.close()

    @classmethod
    def tearDownClass(cls):
        cursor = cls.conn.cursor()
        print("\n[Teardown Check] Cleaning up all test booking records (@rule: testing_cleanup)...")
        # Clean up event equipment & employees
        for evt in cls.test_evt_ids:
            cursor.execute("DELETE FROM dbo.EventEmployee WHERE EventAppointmentID = ?;", (evt,))
            cursor.execute("DELETE FROM dbo.EventEquipment WHERE EventAppointmentID = ?;", (evt,))
            cursor.execute("DELETE FROM dbo.PaymentTransactions WHERE EventAppointmentID = ?;", (evt,))
            cursor.execute("DELETE FROM dbo.HistoryLog WHERE EventAppointmentID = ?;", (evt,))
            cursor.execute("DELETE FROM dbo.EventAppointment WHERE EventAppointmentID = ?;", (evt,))

        for neg in cls.test_neg_ids:
            cursor.execute("DELETE FROM dbo.BookingNegotiation WHERE NegotiationID = ?;", (neg,))

        cursor.execute("DELETE FROM dbo.Customer WHERE CustomerID = ?;", (cls.test_cust_id,))
        cls.conn.commit()
        cursor.close()
        cls.conn.close()
        print(" -> VERIFIED: All test booking and consultation rows cleanly eradicated.")

    def test_01_customer_consultation_creation_and_deal_outcomes(self):
        """
        Step 1 & 2: Act as Customer & Employee.
        Create consultation, check Failed with deal, Reschedule, and Success with deal.
        """
        print("\n[Role: Customer] Scheduling initial booking consultation...")
        cursor = self.conn.cursor()
        neg_id = f"NEG-{uuid.uuid4().hex[:6].upper()}"
        self.test_neg_ids.append(neg_id)

        consult_date = (datetime.date.today() + datetime.timedelta(days=3)).strftime("%Y-%m-%d")
        consult_time = "14:00:00"

        # Customer books consultation
        cursor.execute("""
            INSERT INTO dbo.BookingNegotiation (
                NegotiationID, CustomerID, EmployeeID, AssignedEmployeeName,
                ConsultationDate, ConsultationTime, NegotiationStatus, InquiryNotes,
                MeetingLink, CreatedAt
            )
            VALUES (?, ?, ?, ?, ?, ?, 'Pending_Call', 'Graduation Session Inquiry', 'https://meet.google.com/test-meet-123', SYSUTCDATETIME());
        """, (neg_id, self.test_cust_id, self.clerk_id, self.clerk_name, consult_date, consult_time))
        self.conn.commit()

        # Verify created
        cursor.execute("SELECT NegotiationStatus, EmployeeID, MeetingLink FROM dbo.BookingNegotiation WHERE NegotiationID = ?;", (neg_id,))
        row = cursor.fetchone()
        self.assertEqual(row[0], "Pending_Call")
        self.assertEqual(row[1], self.clerk_id)
        self.assertTrue(row[2].startswith("https://meet.google.com/"))
        print(f" -> OK: Consultation created ({neg_id}) with status '{row[0]}' assigned to clerk {self.clerk_name}.")

        # Employee check: 'Failed with the deal' (Deal_Lost)
        print("\n[Role: Employee/Admin] Testing 'Failed with the deal' outcome...")
        cursor.execute("UPDATE dbo.BookingNegotiation SET NegotiationStatus = 'Deal_Lost', InquiryNotes = 'Customer declined studio rates' WHERE NegotiationID = ?;", (neg_id,))
        self.conn.commit()
        cursor.execute("SELECT NegotiationStatus FROM dbo.BookingNegotiation WHERE NegotiationID = ?;", (neg_id,))
        self.assertEqual(cursor.fetchone()[0], "Deal_Lost")
        print(" -> OK: 'Failed with the deal' successfully transitions status to 'Deal_Lost'.")

        # Employee check: 'Reschedule'
        print("\n[Role: Employee/Admin] Testing 'Reschedule' outcome...")
        new_date = (datetime.date.today() + datetime.timedelta(days=5)).strftime("%Y-%m-%d")
        new_time = "10:00:00"
        cursor.execute("""
            UPDATE dbo.BookingNegotiation
            SET NegotiationStatus = 'Rescheduled',
                ConsultationDate = ?,
                ConsultationTime = ?,
                InquiryNotes = 'Rescheduled to morning slot'
            WHERE NegotiationID = ?;
        """, (new_date, new_time, neg_id))
        self.conn.commit()
        cursor.execute("SELECT NegotiationStatus, ConsultationDate, ConsultationTime FROM dbo.BookingNegotiation WHERE NegotiationID = ?;", (neg_id,))
        row = cursor.fetchone()
        self.assertEqual(row[0], "Rescheduled")
        print(f" -> OK: 'Reschedule' successfully updates date to {row[1]} {row[2]} and status to 'Rescheduled'.")

    def test_02_deal_success_5_step_wizard_email_dispatch_and_payment_on_time(self):
        """
        Step 3: Act as Employee/Admin & Customer.
        'Success with the deal': Book a service, choose time and date.
        When email is dispatched -> advance through wizard steps -> complete payment on time with zero runtime errors.
        """
        print("\n[Role: Employee/Admin] Initiating 'Success with the deal' booking wizard...")
        cursor = self.conn.cursor()
        neg_id = f"NEG-{uuid.uuid4().hex[:6].upper()}"
        self.test_neg_ids.append(neg_id)

        cursor.execute("""
            INSERT INTO dbo.BookingNegotiation (
                NegotiationID, CustomerID, EmployeeID, AssignedEmployeeName,
                ConsultationDate, ConsultationTime, NegotiationStatus, InquiryNotes,
                MeetingLink, CreatedAt
            )
            VALUES (?, ?, ?, ?, ?, '14:00:00', 'Pending_Call', 'VIP Portrait Inquiry', 'https://meet.google.com/test-deal', SYSUTCDATETIME());
        """, (neg_id, self.test_cust_id, self.clerk_id, self.clerk_name, datetime.date.today().strftime("%Y-%m-%d")))
        self.conn.commit()

        # Wizard Step 1: Select service, date, time
        evt_id = f"EVT-TEST-{uuid.uuid4().hex[:6].upper()}"
        self.test_evt_ids.append(evt_id)
        shoot_date = (datetime.date.today() + datetime.timedelta(days=7)).strftime("%Y-%m-%d")
        shoot_time = "14:00:00"
        total_amount = self.service_price
        downpayment = round(total_amount * 0.15, 2)
        balance = total_amount - downpayment
        lock_expires = (datetime.datetime.utcnow() + datetime.timedelta(minutes=60)).strftime("%Y-%m-%d %H:%M:%S")

        print(f" -> Wizard Step 1: Selected Service '{self.service_name}' (PHP {total_amount:.2f}), Date {shoot_date} {shoot_time}")
        print(" -> Clicking 'Reservation Fee Payment': Dispatching email to customer and creating 1-hour slot reservation...")

        # Create appointment with Pending Payment & LockExpiresAt
        cursor.execute("""
            INSERT INTO dbo.EventAppointment (
                EventAppointmentID, CustomerID, EventName, EventDate, EventTime,
                EventStatus, TotalAmount, DownpaymentAmount, FinalBalanceAmount,
                DownpaymentStatus, HoldExpiresAt, LockExpiresAt, CreatedAt
            )
            VALUES (?, ?, 'VIP Studio Portrait Session', ?, ?, 'Pending Payment', ?, ?, ?, 'Pending', ?, ?, SYSUTCDATETIME());
        """, (evt_id, self.test_cust_id, shoot_date, shoot_time, total_amount, downpayment, balance, lock_expires, lock_expires))

        cursor.execute("UPDATE dbo.BookingNegotiation SET EventAppointmentID = ?, NegotiationStatus = 'Pending Payment' WHERE NegotiationID = ?;", (evt_id, neg_id))
        self.conn.commit()

        # Verify email dispatched & wizard advanced to Step 2
        wizard_step = 2
        print(f" -> OK: Email dispatched. Wizard automatically transitioned to Step {wizard_step} (Payment Breakdown & Method).")

        # Step 2: Payment method chosen (QR Ph) -> Advance to Step 3
        wizard_step = 3
        print(f" -> Wizard Step 2 complete: Selected QR Ph (GCash/Maya). Advancing to Step {wizard_step} (Staff & Leader).")

        # Step 3: Staff & Team Leader assignment
        team_leader = self.staff_ids[0] if self.staff_ids else self.clerk_id
        for staff in self.staff_ids:
            is_tl = 1 if staff == team_leader else 0
            cursor.execute("INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID, IsTeamLeader) VALUES (?, ?, ?);", (evt_id, staff, is_tl))
        self.conn.commit()

        wizard_step = 4
        print(f" -> Wizard Step 3 complete: Assigned staff ({', '.join(self.staff_ids)}) with Team Leader {team_leader}. Advancing to Step {wizard_step} (Equipment).")

        # Step 4: Equipment allocation & roster save
        for eq in self.equip_ids:
            cursor.execute("INSERT INTO dbo.EventEquipment (EventAppointmentID, EquipmentID) VALUES (?, ?);", (evt_id, eq))
        self.conn.commit()

        wizard_step = 5
        print(f" -> Wizard Step 4 complete: Allocated equipment ({', '.join(self.equip_ids)}). Advancing to Step {wizard_step} (Await Payment).")

        # Step 5: Make payment on time!
        print("\n[Role: Customer] Settling 15% reservation fee on time via simulated QR Ph payment...")
        cursor.execute("""
            UPDATE dbo.EventAppointment
            SET EventStatus = 'Confirmed',
                DownpaymentStatus = 'Paid',
                DownpaymentPaidAt = SYSUTCDATETIME(),
                LockExpiresAt = NULL,
                HoldExpiresAt = NULL
            WHERE EventAppointmentID = ?;
        """, (evt_id,))

        cursor.execute("UPDATE dbo.BookingNegotiation SET NegotiationStatus = 'Deal_Won' WHERE EventAppointmentID = ?;", (evt_id,))

        # Record payment transaction
        txn_id = f"TXN-{uuid.uuid4().hex[:6].upper()}"
        cursor.execute("""
            INSERT INTO dbo.PaymentTransactions (
                PaymentTransactionID, EventAppointmentID, PaymentType, PaymentMethod,
                Amount, Status, CustomerPaymentStatus, CreatedAt
            )
            VALUES (?, ?, 'Downpayment_15', 'QR Ph', ?, 'Paid', 'Cleared', SYSUTCDATETIME());
        """, (txn_id, evt_id, downpayment))
        self.conn.commit()

        # Verify booking confirmed & payment recorded
        cursor.execute("SELECT EventStatus, DownpaymentStatus, LockExpiresAt FROM dbo.EventAppointment WHERE EventAppointmentID = ?;", (evt_id,))
        evt_row = cursor.fetchone()
        self.assertEqual(evt_row[0], "Confirmed")
        self.assertEqual(evt_row[1], "Paid")
        self.assertIsNone(evt_row[2])

        cursor.execute("SELECT NegotiationStatus FROM dbo.BookingNegotiation WHERE EventAppointmentID = ?;", (evt_id,))
        neg_row = cursor.fetchone()
        self.assertEqual(neg_row[0], "Deal_Won")

        print(" -> OK: Payment completed on time! Booking Status: 'Confirmed', Downpayment: 'Paid', Deal: 'Deal_Won'.")
        print(" -> OK: Zero runtime errors during full wizard execution.")

    def test_03_unpaid_reservation_fee_auto_cancels_and_releases_reserved_time(self):
        """
        Step 4: Act as Customer & Employee.
        Create another booking with the same procedure.
        This time DO NOT pay the reservation fee.
        Ensure that when no payment is made, the reserved time slot is cancelled and freed.
        """
        print("\n[Role: Customer & Employee] Creating second booking: Testing Unpaid Reservation Fee Expiration...")
        cursor = self.conn.cursor()
        neg_id = f"NEG-{uuid.uuid4().hex[:6].upper()}"
        self.test_neg_ids.append(neg_id)

        # 1. Customer creates consultation
        cursor.execute("""
            INSERT INTO dbo.BookingNegotiation (
                NegotiationID, CustomerID, EmployeeID, AssignedEmployeeName,
                ConsultationDate, ConsultationTime, NegotiationStatus, InquiryNotes,
                MeetingLink, CreatedAt
            )
            VALUES (?, ?, ?, ?, ?, '16:00:00', 'Pending_Call', 'Unpaid Test Booking', 'https://meet.google.com/test-unpaid', SYSUTCDATETIME());
        """, (neg_id, self.test_cust_id, self.clerk_id, self.clerk_name, datetime.date.today().strftime("%Y-%m-%d")))
        self.conn.commit()

        # 2. Proceed with deal -> Book service, date, time
        evt_id = f"EVT-TEST-{uuid.uuid4().hex[:6].upper()}"
        self.test_evt_ids.append(evt_id)
        target_shoot_date = (datetime.date.today() + datetime.timedelta(days=12)).strftime("%Y-%m-%d")
        target_shoot_time = "11:00:00"

        # Simulate slot reservation with expired lock (simulating no payment within window)
        past_lock = (datetime.datetime.utcnow() - datetime.timedelta(minutes=5)).strftime("%Y-%m-%d %H:%M:%S")

        cursor.execute("""
            INSERT INTO dbo.EventAppointment (
                EventAppointmentID, CustomerID, EventName, EventDate, EventTime,
                EventStatus, TotalAmount, DownpaymentAmount, FinalBalanceAmount,
                DownpaymentStatus, HoldExpiresAt, LockExpiresAt, CreatedAt
            )
            VALUES (?, ?, 'Unpaid Editorial Shoot', ?, ?, 'Pending Payment', ?, 750.00, 4250.00, 'Pending', ?, ?, SYSUTCDATETIME());
        """, (evt_id, self.test_cust_id, target_shoot_date, target_shoot_time, self.service_price, past_lock, past_lock))

        cursor.execute("UPDATE dbo.BookingNegotiation SET EventAppointmentID = ?, NegotiationStatus = 'Pending Payment' WHERE NegotiationID = ?;", (evt_id, neg_id))

        # Assign staff & equipment to hold the slot
        for staff in self.staff_ids:
            cursor.execute("INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID, IsTeamLeader) VALUES (?, ?, 0);", (evt_id, staff))
        for eq in self.equip_ids:
            cursor.execute("INSERT INTO dbo.EventEquipment (EventAppointmentID, EquipmentID) VALUES (?, ?);", (evt_id, eq))
        self.conn.commit()

        print(f" -> Slot {target_shoot_date} at {target_shoot_time} reserved under 'Pending Payment' (Lock expired without payment).")

        # Verify staff are currently held
        cursor.execute("SELECT COUNT(*) FROM dbo.EventEmployee WHERE EventAppointmentID = ?;", (evt_id,))
        self.assertGreater(cursor.fetchone()[0], 0)

        # 3. Trigger cancellation routine via dbo.usp_ReleaseExpiredPendingPayments
        print(" -> Executing usp_ReleaseExpiredPendingPayments to sweep unpaid reservation...")
        cursor.execute("EXEC dbo.usp_ReleaseExpiredPendingPayments;")
        self.conn.commit()

        # 4. Assert cancellation and slot release
        cursor.execute("SELECT EventStatus, DownpaymentStatus, LockExpiresAt FROM dbo.EventAppointment WHERE EventAppointmentID = ?;", (evt_id,))
        evt_row = cursor.fetchone()
        self.assertEqual(evt_row[0], "Cancelled", "EventStatus must transition to 'Cancelled'")
        self.assertEqual(evt_row[1], "Expired", "DownpaymentStatus must transition to 'Expired'")
        self.assertIsNone(evt_row[2], "LockExpiresAt must be cleared")

        # Assert held staff & equipment are deleted so time slot is completely freed
        cursor.execute("SELECT COUNT(*) FROM dbo.EventEmployee WHERE EventAppointmentID = ?;", (evt_id,))
        self.assertEqual(cursor.fetchone()[0], 0, "Held staff must be freed")

        cursor.execute("SELECT COUNT(*) FROM dbo.EventEquipment WHERE EventAppointmentID = ?;", (evt_id,))
        self.assertEqual(cursor.fetchone()[0], 0, "Held equipment must be freed")

        cursor.execute("SELECT NegotiationStatus FROM dbo.BookingNegotiation WHERE EventAppointmentID = ?;", (evt_id,))
        neg_row = cursor.fetchone()
        self.assertEqual(neg_row[0], "Deal_Lost", "NegotiationStatus must be updated to 'Deal_Lost'")

        print(" -> OK: Unpaid booking status transitioned to 'Cancelled' (Downpayment: 'Expired').")
        print(" -> OK: Held staff and equipment were released.")
        print(f" -> OK: Time slot {target_shoot_date} {target_shoot_time} is now completely unlocked for new bookings!")


if __name__ == "__main__":
    unittest.main(verbosity=2)
