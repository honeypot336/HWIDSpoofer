#include <ntddk.h>
#include <wdm.h>
#include <ntddstor.h>

// Build  : WDK + Visual Studio (x64 Kernel Mode Driver)
// Install: sc create DiskSpoofer type= kernel binPath= "C:\DiskSpoofer.sys"
//          bcdedit /set testsigning on  → reboot → sc start DiskSpoofer

#define FAKE_SERIAL  "HWID00FAKE00SERIAL00"
#define POOL_TAG     'SPFD'

typedef struct _DEV_EXT {
    PDEVICE_OBJECT Lower;
} DEV_EXT, *PDEV_EXT;

// ── Completion: patch serial dans la réponse IOCTL ────────────────────────────

NTSTATUS OnStorageQueryComplete(PDEVICE_OBJECT dev, PIRP irp, PVOID ctx)
{
    UNREFERENCED_PARAMETER(dev);
    UNREFERENCED_PARAMETER(ctx);

    if (NT_SUCCESS(irp->IoStatus.Status))
    {
        PSTORAGE_PROPERTY_QUERY q =
            (PSTORAGE_PROPERTY_QUERY)irp->AssociatedIrp.SystemBuffer;

        if (q && q->PropertyId == StorageDeviceProperty)
        {
            PSTORAGE_DEVICE_DESCRIPTOR desc =
                (PSTORAGE_DEVICE_DESCRIPTOR)irp->AssociatedIrp.SystemBuffer;

            if (desc && desc->SerialNumberOffset != 0 &&
                desc->SerialNumberOffset < desc->Size)
            {
                char *serial = (char *)desc + desc->SerialNumberOffset;
                SIZE_T avail = desc->Size - desc->SerialNumberOffset;
                SIZE_T fakeLen = strlen(FAKE_SERIAL) + 1;

                RtlZeroMemory(serial, avail);
                RtlCopyMemory(serial, FAKE_SERIAL, min(fakeLen, avail));
            }
        }
    }

    if (irp->PendingReturned) IoMarkIrpPending(irp);
    return STATUS_CONTINUE_COMPLETION;
}

// ── Dispatch DeviceIoControl ──────────────────────────────────────────────────

NTSTATUS DispatchDevCtl(PDEVICE_OBJECT dev, PIRP irp)
{
    PDEV_EXT ext   = dev->DeviceExtension;
    PIO_STACK_LOCATION stk = IoGetCurrentIrpStackLocation(irp);

    if (stk->Parameters.DeviceIoControl.IoControlCode ==
        IOCTL_STORAGE_QUERY_PROPERTY)
    {
        IoCopyCurrentIrpStackLocationToNext(irp);
        IoSetCompletionRoutine(irp, OnStorageQueryComplete,
                               NULL, TRUE, TRUE, TRUE);
        return IoCallDriver(ext->Lower, irp);
    }

    IoSkipCurrentIrpStackLocation(irp);
    return IoCallDriver(ext->Lower, irp);
}

// ── Pass-through ──────────────────────────────────────────────────────────────

NTSTATUS DispatchPassThrough(PDEVICE_OBJECT dev, PIRP irp)
{
    IoSkipCurrentIrpStackLocation(irp);
    return IoCallDriver(((PDEV_EXT)dev->DeviceExtension)->Lower, irp);
}

// ── AddDevice: s'attache à chaque disque détecté ─────────────────────────────

NTSTATUS AddDevice(PDRIVER_OBJECT drv, PDEVICE_OBJECT pdo)
{
    PDEVICE_OBJECT filter = NULL;

    NTSTATUS st = IoCreateDevice(drv, sizeof(DEV_EXT), NULL,
                                 FILE_DEVICE_DISK,
                                 FILE_DEVICE_SECURE_OPEN,
                                 FALSE, &filter);
    if (!NT_SUCCESS(st)) return st;

    PDEV_EXT ext  = filter->DeviceExtension;
    ext->Lower    = IoAttachDeviceToDeviceStack(filter, pdo);

    if (!ext->Lower) { IoDeleteDevice(filter); return STATUS_NO_SUCH_DEVICE; }

    filter->Flags |= DO_BUFFERED_IO;
    filter->Flags &= ~DO_DEVICE_INITIALIZING;
    return STATUS_SUCCESS;
}

// ── DriverEntry ───────────────────────────────────────────────────────────────

NTSTATUS DriverEntry(PDRIVER_OBJECT drv, PUNICODE_STRING reg)
{
    UNREFERENCED_PARAMETER(reg);

    for (int i = 0; i <= IRP_MJ_MAXIMUM_FUNCTION; i++)
        drv->MajorFunction[i] = DispatchPassThrough;

    drv->MajorFunction[IRP_MJ_DEVICE_CONTROL]  = DispatchDevCtl;
    drv->DriverExtension->AddDevice             = AddDevice;

    DbgPrint("[DiskSpoofer] loaded — serial will be: " FAKE_SERIAL "\n");
    return STATUS_SUCCESS;
}
