#include "../CaptureProfileFiles.h"
#include <cstring>
#include <iostream>
#include <stdexcept>

using namespace srvsurvey::camera;

namespace
{
void Require(bool condition, const char *message)
{
    if (!condition)
        throw std::runtime_error(message);
}

void WriteBytes(const std::wstring &path, const void *data, DWORD length)
{
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    Require(file != INVALID_HANDLE_VALUE, "Cannot create a profile fixture");
    DWORD written = 0;
    BOOL success = WriteFile(file, data, length, &written, nullptr);
    CloseHandle(file);
    Require(success && written == length, "Cannot write a complete profile fixture");
}

void WriteAscii(const std::wstring &path, const char *text)
{
    WriteBytes(path, text, static_cast<DWORD>(std::strlen(text)));
}

struct Fixture
{
    std::wstring directory, bootstrap, cache, locator;
    Fixture()
    {
        wchar_t temporary[MAX_PATH]{};
        DWORD length = GetTempPathW(MAX_PATH, temporary);
        Require(length > 0 && length < MAX_PATH, "Windows temporary directory unavailable");
        directory = std::wstring(temporary) + L"SrvSurvey-profile-files-" + std::to_wstring(GetCurrentProcessId());
        Require(CreateDirectoryW(directory.c_str(), nullptr) != 0, "Profile fixture directory already exists");
        bootstrap = directory + L"\\SrvSurveyCameraBridge.ini";
        cache = directory + L"\\cached profil\u00e9.ini";
        locator = directory + L"\\SrvSurveyCameraBridge.paths.ini";
    }
    ~Fixture()
    {
        DeleteFileW(bootstrap.c_str());
        DeleteFileW(cache.c_str());
        DeleteFileW((cache + L".previous").c_str());
        DeleteFileW((cache + L".reader-2").c_str());
        DeleteFileW(locator.c_str());
        RemoveDirectoryW(directory.c_str());
    }
    void LocateCache() const
    {
        std::wstring text = L"\ufeff[Updates]\r\nCachedProfilePath=" + cache + L"\r\n";
        WriteBytes(locator, text.data(), static_cast<DWORD>(text.size() * sizeof(wchar_t)));
    }
};
} // namespace

int wmain(int argc, wchar_t **argv)
{
    try
    {
        Fixture fixture;
        WriteAscii(fixture.bootstrap,
                   "[Capture]\nProfileVersion=1\nProtocolVersion=1\nValidated=1\n"
                   "VertexShaderHash=69cbbf4aae46746f\nRigidLayoutVersion=1\nDrawKind=3\n");
        WriteAscii(fixture.cache,
                   "[Capture]\nProfileVersion=2\nProtocolVersion=1\nValidated=1\n"
                   "VertexShaderHash=0123456789abcdef\nRigidLayoutVersion=1\nDrawKind=3\n");
        fixture.LocateCache();
        CaptureProfile loaded = LoadInstalledCaptureProfile(fixture.directory);
        Require(loaded.IsUsable() && loaded.profileVersion == 2 && loaded.shaderHash == 0x0123456789abcdefULL,
                "The Windows INI loader did not select the UTF8 cache through a Unicode path");

        // Isolate installed-profile fallback with no retained reader snapshot.
        DeleteFileW((fixture.cache + L".reader-2").c_str());
        WriteAscii(fixture.cache,
                   "[Capture]\nProfileVersion=3\nProtocolVersion=2\nValidated=1\n"
                   "VertexShaderHash=0123456789abcdef\nRigidLayoutVersion=1\nDrawKind=3\n");
        loaded = LoadInstalledCaptureProfile(fixture.directory);
        Require(loaded.IsUsable() && loaded.profileVersion == 1 && loaded.shaderHash == 0x69cbbf4aae46746fULL,
                "Unsupported cached data prevented fallback to the installed profile");
        WriteAscii(fixture.cache,
                   "[Capture]\nProfileVersion=4\nProtocolVersion=1\nValidated=1\n"
                   "VertexShaderHash=0123456789abcdef\nRigidLayoutVersion=2\nDrawKind=4\n"
                   "[Layout]\nSchemaVersion=1\nRequiredBridgeVersion=2\nShaderStage=ps\n"
                   "RecordStage=gs\nRecordSlot=7\nCameraStage=ps\nCameraSlot=3\n"
                   "BufferKind=raw\nBufferElementStride=4\nRecordStride=512\nPositionOffset=64\n"
                   "RotationFormat=float32\nRotationOrder=wxyz\nPositionFormat=float64\n"
                   "CameraBytes=8192\nProjectionOffset=512\nProjectionFormat=float64\nProjectionTranspose=1\n");
        loaded = LoadInstalledCaptureProfile(fixture.directory);
        Require(loaded.IsUsable() && loaded.profileVersion == 4 && loaded.layout.shaderStage == 1 &&
                loaded.layout.recordStage == 2 && loaded.layout.recordSlot == 7 && loaded.layout.cameraStage == 1 &&
                loaded.layout.cameraSlot == 3 && loaded.drawKind == 4 && loaded.layout.recordStride == 512 &&
                loaded.layout.positionOffset == 64 && loaded.layout.positionFormat == 2 && loaded.layout.rotationOrder == 1 &&
                loaded.layout.cameraBytes == 8192 && loaded.layout.projectionOffset == 512 && loaded.layout.projectionTranspose == 1,
                "The Windows INI loader did not apply the declarative buffer layout");
        Require(CopyFileW(fixture.cache.c_str(), (fixture.cache + L".previous").c_str(), FALSE) != 0,
                "Cannot seed the previous compatible catalog");
        WriteAscii(fixture.cache,
                   "[Capture]\nProfileVersion=5\nProtocolVersion=1\nValidated=1\n"
                   "VertexShaderHash=1111111111111111\nRigidLayoutVersion=2\nDrawKind=3\n"
                   "[Layout]\nSchemaVersion=1\nRequiredBridgeVersion=99\nFuturePacking=1\n");
        loaded = LoadInstalledCaptureProfile(fixture.directory);
        Require(loaded.IsUsable() && loaded.profileVersion == 4 && loaded.layout.recordStride == 512,
                "An unsupported future catalog did not fall back to its compatible predecessor");
        WriteAscii((fixture.cache + L".previous"),
                   "[Capture]\nProfileVersion=5\nProtocolVersion=1\nValidated=1\n"
                   "VertexShaderHash=1111111111111111\nRigidLayoutVersion=2\nDrawKind=3\n"
                   "[Layout]\nSchemaVersion=1\nRequiredBridgeVersion=99\nFuturePacking=1\n");
        WriteAscii(fixture.cache,
                   "[Capture]\nProfileVersion=6\nProtocolVersion=1\nValidated=1\n"
                   "VertexShaderHash=2222222222222222\nRigidLayoutVersion=2\nDrawKind=3\n"
                   "[Layout]\nSchemaVersion=1\nRequiredBridgeVersion=100\nFuturePacking=1\n");
        loaded = LoadInstalledCaptureProfile(fixture.directory);
        Require(loaded.IsUsable() && loaded.profileVersion == 4 && loaded.layout.recordStride == 512,
                "Successive unsupported catalogs discarded the last decoder-usable profile");
        DeleteFileW(fixture.locator.c_str());
        Require(LoadInstalledCaptureProfile(fixture.directory).shaderHash == 0x69cbbf4aae46746fULL,
                "A missing cache locator prevented the installed profile from loading");
        if (argc == 2)
        {
            Require(CopyFileW(argv[1], fixture.bootstrap.c_str(), FALSE) != 0, "Cannot load the supplied catalog fixture");
            loaded = LoadInstalledCaptureProfile(fixture.directory);
            Require(loaded.IsUsable() && loaded.profileVersion == 2 && loaded.layoutVersion == 2 &&
                    loaded.shaderHash == 0x69cbbf4aae46746fULL && loaded.layout.recordStride == 336 &&
                    loaded.layout.recordSlot == 33 && loaded.layout.cameraSlot == 1 &&
                    loaded.layout.projectionOffset == 4320 && loaded.layout.rotationFormat == 1,
                    "The actual published-source catalog did not load with the captured layout");
        }
        std::cout << "PASS: real Windows INI cache loading, Unicode paths and compatible fallback" << std::endl;
        return 0;
    }
    catch (const std::exception &exception)
    {
        std::cerr << "FAIL: " << exception.what() << std::endl;
        return 1;
    }
}
