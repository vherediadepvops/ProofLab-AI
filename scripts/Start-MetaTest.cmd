@echo off
setlocal
title ProofLab AI - Meta Model API - TEMPORARY LOCAL TEST
if /I not "%COMPUTERNAME%"=="DESKTOP-ESGSLJH" (
  echo ERROR: This test may run only on authorized DESKTOP-ESGSLJH.
  pause
  exit /b 1
)
pushd "%~dp0..\src\ProofLab.Api" || exit /b 1
if not exist "bin\Release\net10.0\ProofLab.Api.exe" (
  echo ERROR: Build the ProofLab API Release version first.
  popd
  pause
  exit /b 1
)
if not defined MODEL_API_KEY (
  echo ERROR: MODEL_API_KEY was not inherited by this new Windows terminal.
  echo Check the existing Windows environment variable. Never paste the key here.
  echo Sign out and back in if necessary, then start a fresh CMD window.
  popd
  pause
  exit /b 1
)
echo.
echo PROOFLAB AI - OPTIONAL META MODEL API SESSION
echo.
echo This script starts ONLY a temporary API on http://127.0.0.1:5393
echo Main ProofLab API on port 5391 stays unchanged and payment-disabled.
echo The models may use Meta API credits or bill a linked payment method.
echo Visible free credits are NOT a hard monetary cap.
echo Each temporary API process permits MAXIMUM TWO model requests total.
echo Close this window or press Ctrl+C to stop. Do not restart to bypass budgets.
echo.
set "PROOFLAB_META_CONFIRM="
set /p "PROOFLAB_META_CONFIRM=After checking Meta Billing, type ACTIVAR to approve possible charges: "
if /I not "%PROOFLAB_META_CONFIRM%"=="ACTIVAR" (
  echo Session not enabled.
  popd
  exit /b 0
)
set "PROOFLAB_META_CONFIRM="
set "Meta__CreditsConfirmedByOwner=true"
set "Meta__OwnerApprovedPaidRequests=true"
set "Meta__AllowPaidRequests=true"
echo.
echo Optional session enabled. Open ProofLab AI, select Pruebas Meta API,
echo then click Comprobar conexion de prueba. No calls occur on startup.
echo Each model action asks for your confirmation and shows its result.
echo.
"bin\Release\net10.0\ProofLab.Api.exe" --urls http://127.0.0.1:5393
echo.
echo The temporary test server has stopped.
set "Meta__CreditsConfirmedByOwner="
set "Meta__OwnerApprovedPaidRequests="
set "Meta__AllowPaidRequests="
popd
pause
endlocal
