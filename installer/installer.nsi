Unicode true

!define APPNAME "VRCFaceTracking"
!define EXENAME "VRCFaceTracking.exe"
!define UNINSTALLER "Uninstall.exe"
!define ARPKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRCFaceTracking"
!define DATADIR "$APPDATA\VRCFaceTracking"
!define LOCALDIR "$LOCALAPPDATA\VRCFaceTracking"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\VRCFaceTracking"
InstallDirRegKey HKCU "${ARPKEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!define MUI_ICON "..\VRCFaceTracking\Assets\WindowIcon.ico"
!define MUI_UNICON "..\VRCFaceTracking\Assets\WindowIcon.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "Start VRCFaceTracking"

!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

VIProductVersion "${VIVERSION}"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "LegalCopyright" ""

Var Upgrade

!macro IsRunning EXE
	nsExec::ExecToStack 'cmd /c tasklist /NH /FI "IMAGENAME eq ${EXE}" | find /I "${EXE}"'
	Pop $0
	Pop $3
!macroend

!macro GuardRunning
	${Do}
		StrCpy $1 ""
		!insertmacro IsRunning "${EXENAME}"
		${If} $0 = 0
			StrCpy $1 "VRCFaceTracking is running. Close it, then try again."
		${Else}
			!insertmacro IsRunning "VRCFaceTracking.ModuleProcess.exe"
			${If} $0 = 0
				StrCpy $1 "A VRCFaceTracking module is still closing. Wait a few seconds, then try again."
			${EndIf}
		${EndIf}
		${If} $1 == ""
			${Break}
		${EndIf}
		${If} ${Silent}
			SetErrorLevel 5
			Quit
		${EndIf}
		MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$1" IDRETRY +2
		Quit
	${Loop}
!macroend

Function .onInit
	!insertmacro GuardRunning
FunctionEnd

Function un.onInit
	${GetParameters} $R0
	ClearErrors
	${GetOptions} $R0 "/UPGRADE" $R1
	${IfNot} ${Errors}
		StrCpy $Upgrade 1
	${EndIf}
	!insertmacro GuardRunning
FunctionEnd

Section "Install"
	ReadRegStr $0 HKCU "${ARPKEY}" "InstallLocation"
	${If} $0 != ""
	${AndIf} ${FileExists} "$0\${UNINSTALLER}"
		DetailPrint "Removing the previous version from $0"
		${If} $0 == $INSTDIR
			ExecWait '"$0\${UNINSTALLER}" /S /UPGRADE _?=$0' $1
		${Else}
			ExecWait '"$0\${UNINSTALLER}" /S _?=$0' $1
		${EndIf}
		${If} $1 != 0
			DetailPrint "The previous uninstaller returned $1"
		${EndIf}
		Delete "$0\${UNINSTALLER}"
		RMDir "$0"
	${EndIf}

	SetOutPath "$INSTDIR"
	File /r "${PAYLOAD}\*"
	WriteUninstaller "$INSTDIR\${UNINSTALLER}"
	CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"

	WriteRegStr HKCU "${ARPKEY}" "DisplayName" "${APPNAME}"
	WriteRegStr HKCU "${ARPKEY}" "DisplayVersion" "${VERSION}"
	WriteRegStr HKCU "${ARPKEY}" "DisplayIcon" "$INSTDIR\${EXENAME}"
	WriteRegStr HKCU "${ARPKEY}" "Publisher" "RealWhyKnot"
	WriteRegStr HKCU "${ARPKEY}" "InstallLocation" "$INSTDIR"
	WriteRegStr HKCU "${ARPKEY}" "UninstallString" '"$INSTDIR\${UNINSTALLER}"'
	WriteRegStr HKCU "${ARPKEY}" "QuietUninstallString" '"$INSTDIR\${UNINSTALLER}" /S'
	WriteRegStr HKCU "${ARPKEY}" "URLInfoAbout" "https://github.com/RealWhyKnot/VRCFaceTracking"
	WriteRegDWORD HKCU "${ARPKEY}" "NoModify" 1
	WriteRegDWORD HKCU "${ARPKEY}" "NoRepair" 1
	${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
	WriteRegDWORD HKCU "${ARPKEY}" "EstimatedSize" $0

	DetailPrint "Registering with SteamVR..."
	nsExec::ExecToLog /TIMEOUT=60000 '"$INSTDIR\${EXENAME}" --register-steamvr'
	Pop $0
	${If} $0 == "3"
		DetailPrint "SteamVR is not installed. VRCFaceTracking registers itself the first time it runs with SteamVR open."
	${ElseIf} $0 != "0"
		DetailPrint "SteamVR registration did not finish ($0). VRCFaceTracking registers itself the next time it runs with SteamVR open."
	${EndIf}
SectionEnd

Section "Uninstall"
	${If} $Upgrade != 1
		DetailPrint "Removing VRCFaceTracking from SteamVR..."
		nsExec::ExecToLog /TIMEOUT=60000 '"$INSTDIR\${EXENAME}" --unregister-steamvr'
		Pop $0
	${EndIf}

	!include /CHARSET=UTF8 "${FILELIST}"

	${If} $Upgrade == 1
		Return
	${EndIf}

	Delete "$SMPROGRAMS\${APPNAME}.lnk"
	DeleteRegKey HKCU "${ARPKEY}"
	Delete "$INSTDIR\${UNINSTALLER}"
	RMDir "$INSTDIR"
	RMDir /r "${LOCALDIR}\update"
	RMDir "${LOCALDIR}"

	${IfNot} ${Silent}
		MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Also delete your VRCFaceTracking settings, installed modules and logs?$\r$\n$\r$\nThe Steam version of VRCFaceTracking uses the same settings and modules. Choose No to keep them.$\r$\n$\r$\n${DATADIR}$\r$\n${LOCALDIR}" IDNO keep_data
		RMDir /r "${DATADIR}"
		RMDir /r "${LOCALDIR}"
		keep_data:
	${EndIf}
SectionEnd
