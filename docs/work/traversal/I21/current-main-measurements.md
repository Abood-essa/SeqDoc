# I21 current-main measurement packet

Approved temporary outputs only; no generated artifact content is copied. Paths are relative to each lane's `output`.
Run 1 uses `credit-1`, `fraud-1`, `sms-1`, and `ticket-1`; run 2 uses the matching `*-2` directory.

Metric: UTF-16/.NET character count is `File.ReadAllText(path).Length`. Material messages use the exact multiline
regex `^\s*[^\r\n:]+->>[^\r\n:]+:`. Participants use `^\s*participant\s+`; fragment openers use
`^\s*(?:alt|opt|loop|par|critical|break)\b`. Every listed Mermaid has a same-stem Markdown file.

## Run-1 file measurements

Columns: `lane | filename | chars/messages/participants/fragments | SHA256 | Markdown`.

```text
CT|credittransferengine-businesslogic-credittransfer-transfercredit-string-string-decimal-string-string-ref-string-52a13375.mmd|4821/44/5/15|f1c45873f929acacf0e156a275e219e48bd68682a14b41c19ef8f661807d4977|yes
CT|credittransferengine-businesslogic-credittransfer-transfercreditwithadjustmentreason-string-string-decimal-string-string-string-ref-string-d7164f46.mmd|4601/41/5/13|df24415d90a03fcad8ba660539305cb0d40007abcda9a5d3495b02947258156c|yes
CT|credittransferengine-businesslogic-transactionmanager-getalltransactionprocessing-d4b45c03.mmd|88/0/1/0|758e018c139d062a39238b69941409360596ec4e9d9b6cd08796a3ad4059d9b4|yes
CT|credittransferengine-businesslogic-transactionmanager-processtransactions-2460de2c.mmd|1410/9/5/3|2be79f1d951ce7769ef5ab488a64e89117af9eed13a6a73dfc144a30094d3a4b|yes
CT|credittransferservices-credittransferservice-transfercredit-string-string-int-int-string-out-int-out-string-d6245f78.mmd|241/1/2/0|28c89c26935205d2c28fce19081c30bda131d2e6ec3485a7422706e1718d92e0|yes
CT|credittransferservices-credittransferservice-transfercreditwithoutpinforsc-string-string-decimal-out-int-out-string-413b3090.mmd|266/1/2/0|3adb098dfe2d01dfbe06f91b7d0a463c0fc919b0fb2812f250044696bacd556f|yes
CT|post-ussd-e451d77c.mmd|419/2/4/1|1eb89eb617a2abb56f2508beb568c35f1642e9b6740867841a71239918d8faa4|yes
CT|post-virgincredittransfer-6f3f6b5c.mmd|441/2/4/1|e1c36d5003743cb5049c74197c9f1d6a4dd7bf65b6a5f2bdfcd3e0247a8e89bb|yes
Fraud|bll-communicationproxycontroller-addorupdatereportedrequest-dal-reportedrequest-18ded438.mmd|175/1/2/0|4d3eb7583ca8eceda83f76e2acffa252c4537f6cfd9f20f21520140e0f8c24dc|yes
Fraud|bll-nps-npsintegration-pushsurveyfruadnotification-string-string-string-bll-enums-brand-dcf73e3d.mmd|704/5/5/0|67ddeac01908e49ae05a510e4c7c629e036fcaa305411b06cbbdb154c8d38588|yes
Fraud|bll-reportedcustomerscontroller-getcustomersdata-system-datetime-system-datetime-system-collections-generic-list-bll-enums-requesttype-a3009e63.mmd|370/2/3/0|605a12080ece076023541676f41c8b6d3d4293ff166088da52fbbc312b49c950|yes
Fraud|bll-reportedcustomerscontroller-getcustomerseligibleforunblocking-c1ed7ec9.mmd|181/1/2/0|3b21a2283e05e18fc9708515098107130575e840690363568f81db6a7e5ddc46|yes
Fraud|bll-reportedcustomerscontroller-getsmscontentdata-70db6395.mmd|371/2/3/0|c51e07563c970c8c2ec42f7bfe004f6fae4c75e137fb12162393269318681bec|yes
Fraud|bll-reportedcustomerscontroller-insertcomplaint-bll-enums-reportingchannel-int-string-bool-bool-int-int-int-system-datetime-string-int-string-string-bool-abdc804d.mmd|384/1/5/0|51f7fdbddfa1f677a529a4c3f3ee983693a63c6ce9eb61c2f282f42509c28efd|yes
Fraud|bll-reportedcustomerscontroller-insertreportedcustomer-dal-reportedcustomer-out-int-dd4a2ee9.mmd|170/1/2/0|0fab24db19b1660157ada9d2b33f1d7a8f43583fe46dc5abe53b2e85de1eaf23|yes
Fraud|bll-reportedcustomerscontroller-unblockreportedcustomer-int-string-out-string-out-string-43be4f3e.mmd|1064/7/6/1|0d64c0ec02a38798ac1b7f2b0c1674bf81ce0772d374cb248dcd2d857b5c7d49|yes
Fraud|bll-reportedcustomerscontroller-updatereportedcustomer-int-system-datetime-decimal-bool-bool-bool-int-string-int-system-datetime-string-390fe961.mmd|170/1/2/0|23a02b9bfcd34be08dbf0145adca9dae8845b56e17afbc164def5562a9e216e1|yes
Fraud|bll-tccintegration-tccservice-addcomplaint-bll-tccintegration-addcomplaintrequest-f1cc2038.mmd|260/2/3/0|8e5db1e0d925754ebd19e494c6ac8d249ad30ba3c6237113ff62311ca8a63e2b|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-getcustomersdata-fraudmanagementapigateway-datacontracts-getcustomerdatarequest-3d07f0aa.mmd|301/2/3/0|13210904d8b3a018912cc603a7cdf125ca42775301b53e8c05ca43393240d07a|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-getlookups-string-1ab08e5f.mmd|518/6/3/0|fe97e9697b42761b03e72beac99fcf62585886e05554f13cb7b796a5e1872d67|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-getreportedcustomerdata-fraudmanagementapigateway-datacontracts-getreportedcustomerdata-getreportedcustomerdatarequest-50095938.mmd|310/2/3/0|e60be326bf0bbf6cbd1e7da155a859846011c70bef01441de6afef68a8beffe2|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-getreportedcustomersdatalist-fraudmanagementapigateway-datacontracts-getreportedcustomerdata-getreportedcustomersdatalistrequest-daf5c3cc.mmd|611/7/3/0|c0c349d6726d2935f33def8e8df6864a65bb55371ed0b1e4578c83d1a450e916|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-getsmscontentdata-18d71b49.mmd|303/2/3/0|c958311c2af6f91d1749f84b4454fe7b865510c103e919810a484cd58e4eea0e|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-insertfraud-fraudmanagementapigateway-datacontracts-insertfraudrequest-2dbd1789.mmd|1030/7/9/1|8d9deaa3a215aafdeb10e2f88094d1596bd58c6b9279d81690ac795fd7f57486|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-pushsematinotification-fraudmanagementapigateway-datacontracts-pushsematinotificationrequest-cc87cb6f.mmd|165/1/2/0|c0b37c28138826bf9b20b7bfb2c1db1a90eb1c2ca0a20b480f6db7f84bfa34bd|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-setincidentspamstatus-fraudmanagementapigateway-datacontracts-setincidentspamstatus-setincidentspamstatusrequest-e58e0e9e.mmd|312/2/3/0|16f2f2da75fde6703bfca0a68363c21e419f794398741f4a24196c793144c774|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-submitfeedbacktotcc-fraudmanagementapigateway-datacontracts-tccfeedbackrequest-abdb5e57.mmd|1128/11/6/2|4766833dffcc129155d6040b673fec64eba21a9b8952b439f734407f739cf31f|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-submitfraudsurveyfeedback-fraudmanagementapigateway-datacontracts-submitfraudsurveyfeedbackrequest-d7d4883b.mmd|1227/13/6/2|a96fd32d3abd9890b7e267295133fcb232f4c854181e754e92bf80fe698ceafd|yes
Fraud|fraudmanagementapigateway-integrationwrappers-fraudmanagementwrapper-unblockreportedcustomer-fraudmanagementapigateway-datacontracts-unblockreportedcustomerrequest-acbf7524.mmd|379/2/4/0|3313865077aa682689ce7f7856f734f756a40827b35738651687e4c8b25d5e1f|yes
Fraud|get-fraudmanagementservice-getlookups-f6fbc325.mmd|379/2/3/0|c48b8089fa3ea057aa44e5448ebdad6f24f5dbc2f3b3d143e3197df3ee1ea8d6|yes
Fraud|get-fraudmanagementservice-getsmscontentdata-540293b4.mmd|400/2/3/0|c5c45ee8219ee3662dd8e999bf0bc811c9b2329c813aabc3b96df9769335a6f0|yes
Fraud|get-fraudsmsdispatcher-aspx-b91a0e12.mmd|367/3/4/0|7de529aa49faa058719debc1ea21f00a9484987e9755fa94203b219dd2593c26|yes
Fraud|get-smsdispatcher-dispatch-cae0db2c.mmd|366/3/4/0|7e4e5a37df4113ebd6afce11192b5d8fcc4d5c5af9962d02e327a43ae89c5644|yes
Fraud|hosted-worker-fraudmanagementwindowsservice-worker-89186e40.mmd|225/1/3/0|1357271040f6650cbae7bc6d7df3e7518cac076e26cd08c2fc8b4183153b407d|yes
Fraud|post-api-submitfeedbacktotcc-38775566.mmd|248/1/3/0|8389d3d7b48fc2bab605156f14797277a8ed1fdf836b14aaa87603443aea1014|yes
Fraud|post-fraudmanagementservice-getcustomerdata-9206fb8d.mmd|396/2/3/0|c17a8e8ae1eeee01b5c643a77f0450363a16678cc3a77f951489fc53c25d4067|yes
Fraud|post-fraudmanagementservice-getreportedcustomerdata-94bd783f.mmd|419/2/3/0|d82f8acd423f5c3dbbc59f5e6c83ddd0f1457b1d13df03fb730b1592211ac145|yes
Fraud|post-fraudmanagementservice-getreportedcustomersdatalist-14444dae.mmd|434/2/3/0|7f04ae49d6579f2499e69b773ce368ea72450c57fb0f59b01b9ae7a34c33d989|yes
Fraud|post-fraudmanagementservice-insertfraud-4f2be324.mmd|383/2/3/0|799c317277cc9aa4dff1f928d8d5fa36da1a5436512393008701b1fb3f5a4bd9|yes
Fraud|post-fraudmanagementservice-pushsematinotification-8beed2e2.mmd|416/2/3/0|892334ddaf816fc48be471494588dd36b0d7e0dedcbf8044e4993215b7d6a936|yes
Fraud|post-fraudmanagementservice-setincidentspamstatus-138f428d.mmd|413/2/3/0|6724c10a7ec527c54385316ee2f6f5dbe2b4ab4a5c095aea5937f56d284a91cc|yes
Fraud|post-fraudmanagementservice-submitfeedbacktotcc-f40f3356.mmd|407/2/3/0|619ea70ce08f3303db7ca4cb5aa394ff2186bc6bbfb9045520f501f5032f42f8|yes
Fraud|post-fraudmanagementservice-submitfraudsurveyfeedback-dbe02cbe.mmd|425/2/3/0|a587ab6c7cb6afa92d567624b7e96414e497105fecade00bf6531e627c12b29b|yes
Fraud|post-fraudmanagementservice-unblockreportedcustomer-cea1f1f6.mmd|419/2/3/0|007c22999cdd643cbceb6a4424a63ed3af7025c69317a97cbd3b94421aaab483|yes
SMS|hosted-worker-lp-smsgateway-windowshost-smsgatewayworker-59728dbc.mmd|247/1/3/0|609d52f6b637fbf8ff1a3e8595bf146cb76f9b0b9b5fb7bf185ce3b6959aa25a|yes
SMS|lp-messaging-sms-smsccluster-sendsms-lp-messaging-sms-smsmessage-int-efe79aaf.mmd|158/1/2/0|138a16e2b5de5fe412dd6e28c96187aa56000e2fad3992f471fbbd3043ed4207|yes
SMS|lp-messaging-sms-smscnode-processdeliversm-int-string-string-string-string-byte-byte-bool-byte-string-80e7f115.mmd|1907/24/4/2|f0aeabcc5d5d8e5a3ed99c1c5ace7abb30fb340b41d9ecac17be6ecf083c5e14|yes
SMS|lp-messaging-sms-smscnode-receive-d400bba2.mmd|153/1/2/0|5c17a63aecef35e1f82e939685782f2e1eceac3f6152a40dbea245cef94f756a|yes
SMS|lp-smsgateway-common-jobfileprocessor-processfile-system-io-stream-string-int-bool-deb4c169.mmd|1569/15/6/1|76880d43a68f9b1ce8d59362db44b683e6c3b6bb298efebdca2d2dbc2e3acb87|yes
SMS|lp-smsgateway-manager-jobscontroller-start-08580486.mmd|666/5/4/1|d19bedb1348c85711672c8d9e405d33ebfa9caec7ed498473f79952c96599714|yes
SMS|lp-smsgateway-manager-smscontroller-start-4c6d30e1.mmd|457/3/3/0|535ed97a94d4cde57d0ab1044e730196a2ca7af042f2435407aafc872765e4fc|yes
SMS|lp-smsgateway-manager-smsgatewayextservice-sendsms-int-int-string-string-string-int-fcc8fe48.mmd|609/4/5/0|77a29aa95bd591777621554dc29621f0d3ef840f43a3a9ec4861fb441fb5b084|yes
SMS|lp-smsgateway-manager-smsgatewayextservice-sendsmsbatch-int-int-string-system-collections-generic-list-string-string-system-collections-generic-list-system-collections-generic-list-string-int-2ff2aa70.mmd|619/4/5/0|c0c208167c76e082d46b4925fc8f67506271993eaf4d51a9f885d5a62f82bc81|yes
SMS|lp-smsgateway-manager-smsgatewayextservice-sendsmsbatchwithtemplatename-int-int-string-system-collections-generic-list-string-string-system-collections-generic-list-system-collections-generic-list-string-int-963fc288.mmd|922/6/6/0|d5d68f78206117c5bb5d2597fa44602b95effd11e69511afa143439f43f5cb56|yes
SMS|lp-smsgateway-manager-smsgatewayextservice-sendsmswithtemplate-int-int-string-string-string-system-collections-generic-list-string-int-7cda990e.mmd|989/7/6/0|ea2a7910f350e700bcb2b7f0b3c2089923bcf47cfbb4af1d399e010697483f97|yes
SMS|lp-smsgateway-manager-smsgatewaywcfservice-getreceivedmessages-string-system-datetime-system-datetime-a40d3d0b.mmd|451/3/3/0|9ef620bf0229d415402cf167fd759fbf2e8772c15f65c3855d131bf1345c1c7a|yes
SMS|lp-smsgateway-manager-smsgatewaywcfservice-getschedulejobs-lp-smsgateway-common-jobstate-int-42794e0e.mmd|444/3/3/0|9928d3fca72a92c53aaeb6832896d116caae7bbc8fc13a683fbbebb5b304faa3|yes
SMS|lp-smsgateway-manager-smsgatewaywcfservice-schedulenewjob-lp-smsgateway-common-smsjobcontract-dd299b01.mmd|1132/8/6/1|845490d15e30498a7cbe36d54e31d21761138ed51f6ff7f388ebc75c22099da5|yes
SMS|lp-smsgateway-manager-smsgatewaywcfservice-updatejob-lp-smsgateway-common-smsjobcontract-3eb57ae9.mmd|1375/10/6/2|92a4de76234e7c389c7597dbb420fcb6ebf200b6f31977fc6b9a0d91c85f1efe|yes
Ticket|delete-api-reservations-id-guid-2a3178da.mmd|387/4/4/0|5aefb6813aa8a07b72bf59f97e3da3b2709528a88d6eedbe39f13cfe2eb37216|yes
Ticket|get-api-reservations-id-guid-e362b674.mmd|484/5/4/1|4f8ccaecb768a78d18fe6289e007e89e71cc35740a078b704fc71eb3e29d3626|yes
Ticket|post-api-reservations-27c5d339.mmd|367/4/4/0|c298bcece5849f68dcca8c471fba48502f760ba97f796d45187e92bbe023d88f|yes
Ticket|put-api-reservations-id-guid-5684132b.mmd|384/4/4/0|2f8b5e03d5f0457e52dd2376df962841ad761ef7dc8b30b441fe9d1891d4b9e5|yes
```

## Totals, hashes, and results

| lane | target / profile / fingerprint | files / mmd | total chars / max | messages / participants / fragments | run-1 manifest SHA256 | run-2 | equality |
|---|---|---:|---:|---:|---|---|---|
| CT | Web / `3f0c16b6` / `dd9b9153` | 18 / 8 | 12,287 / 4,821 | 100 / 28 / 33 | `b628f7ecd411cc35976c1aab977c8ff18b68ac15eb5536c776cbddd0bfa00226` | same | yes |
| Fraud | `FraudManagement.sln` / `f874be7e` / `f9a36fd5` | 74 / 36 | 15,830 / 1,227 | 108 / 125 / 6 | `901cd849f469b020891d78a935fabe3e5978e343ef8dcf3bd48ae61ca8aa01b2` | same | yes |
| SMS | WindowsHost / `a82776da` / `f1bfaa65` | 32 / 15 | 11,698 / 1,907 | 95 / 64 / 7 | `af402d29766d973afeac1a1649953e1b019e229ff2550b67016261b7d600841c` | same | yes |
| Ticket | API / `417880f3` / `0e5e3ea2` | 10 / 4 | 1,622 / 484 | 17 / 16 / 1 | `670ed27cdbc811b14fe6dde41612030009e436278611248667880c25169de877` | same | yes |

All 63 Mermaid files have Markdown, broken links are 0, and budget violations are 0 (45,000-character limit).
Mermaid CLI 11.16.0 rendered 63/63 run-1 files to SVG with 0 failures. Diagnostic census from CLI output: CT
1,206 warnings/0 errors; Fraud 5,884/0; SMS 1,677/0; Ticket 216/0. Displayed codes/advisories are reported only as
conservative warnings (including applicability/load and unsupported-boundary advisories where shown); no complete code
distribution is claimed because the current CLI omitted the diagnostic artifact. That wording is separate I24 work.

## Reproducible commands

Run from the SeqDoc root. `$SEQDOC_TEST_PROJECTS_ROOT` is the environment variable or the standard sibling checkout;
all output/cache paths are disposable variables. External revisions are CT `02b82a5115ef6e2d138c70670f28b959fb646f6e`,
Fraud `7aabfef98fa4d47781bd8a98b9061ddcafb88836`, SMS `7ca797356b1856eb815922ca977e9d85a569cb84`, Ticket
`1e25b6943a7dcfc443b8dca2ea946ee28afe811f4`.

```powershell
$SEQDOC_ROOT = (Resolve-Path '.').Path
$SEQDOC_TEST_PROJECTS_ROOT = if ($env:SEQDOC_TEST_PROJECTS_ROOT) { [IO.Path]::GetFullPath($env:SEQDOC_TEST_PROJECTS_ROOT) } else { (Resolve-Path (Join-Path $SEQDOC_ROOT '..\SeqDoc-TestProjects')).Path }
$APPROVED_TEMP_ROOT = Join-Path ([IO.Path]::GetTempPath()) 'seqdoc-i21'
New-Item -ItemType Directory -Force -Path $APPROVED_TEMP_ROOT | Out-Null
$CT_REPO = Join-Path $SEQDOC_TEST_PROJECTS_ROOT 'Provided\CreditTransfer-om'
$FRAUD_REPO = Join-Path $SEQDOC_TEST_PROJECTS_ROOT 'Provided\FraudManagement'
$SMS_REPO = Join-Path $SEQDOC_TEST_PROJECTS_ROOT 'Provided\SMSGateway-om'
$TICKET_REPO = Join-Path $SEQDOC_TEST_PROJECTS_ROOT 'Provided\TicketReservation-Solution'
$CT_TARGET = Join-Path $CT_REPO 'CreditTransferWeb\CreditTransfer.csproj'; $CT_CONFIG = Join-Path $SEQDOC_ROOT 'docs\examples\credit-transfer.yaml'; $CT_FRAMEWORK = 'net9.0'
$FRAUD_TARGET = Join-Path $FRAUD_REPO 'FraudManagement.sln'; $FRAUD_CONFIG = Join-Path $SEQDOC_ROOT 'docs\examples\fraud-management.yaml'; $FRAUD_FRAMEWORK = 'net9.0'
$SMS_TARGET = Join-Path $SMS_REPO 'Source\LP.SMSGateway.WindowsHost\LP.SMSGateway.WindowsHost.csproj'; $SMS_CONFIG = Join-Path $SEQDOC_ROOT 'docs\examples\sms-gateway.yaml'; $SMS_FRAMEWORK = 'net9.0'
$TICKET_TARGET = Join-Path $TICKET_REPO 'TicketReservation.Api\TicketReservation.Api.csproj'; $TICKET_FRAMEWORK = 'net10.0'
$CT_RUN_1 = Join-Path $APPROVED_TEMP_ROOT 'credit-1'; $CT_RUN_2 = Join-Path $APPROVED_TEMP_ROOT 'credit-2'; $CT_OUT_1 = Join-Path $CT_RUN_1 'output'; $CT_OUT_2 = Join-Path $CT_RUN_2 'output'; $CT_CACHE_1 = Join-Path $CT_RUN_1 'cache-v1.db'; $CT_CACHE_2 = Join-Path $CT_RUN_2 'cache-v1.db'
$FRAUD_RUN_1 = Join-Path $APPROVED_TEMP_ROOT 'fraud-1'; $FRAUD_RUN_2 = Join-Path $APPROVED_TEMP_ROOT 'fraud-2'; $FRAUD_OUT_1 = Join-Path $FRAUD_RUN_1 'output'; $FRAUD_OUT_2 = Join-Path $FRAUD_RUN_2 'output'; $FRAUD_CACHE_1 = Join-Path $FRAUD_RUN_1 'cache-v1.db'; $FRAUD_CACHE_2 = Join-Path $FRAUD_RUN_2 'cache-v1.db'
$SMS_RUN_1 = Join-Path $APPROVED_TEMP_ROOT 'sms-1'; $SMS_RUN_2 = Join-Path $APPROVED_TEMP_ROOT 'sms-2'; $SMS_OUT_1 = Join-Path $SMS_RUN_1 'output'; $SMS_OUT_2 = Join-Path $SMS_RUN_2 'output'; $SMS_CACHE_1 = Join-Path $SMS_RUN_1 'cache-v1.db'; $SMS_CACHE_2 = Join-Path $SMS_RUN_2 'cache-v1.db'
$TICKET_RUN_1 = Join-Path $APPROVED_TEMP_ROOT 'ticket-1'; $TICKET_RUN_2 = Join-Path $APPROVED_TEMP_ROOT 'ticket-2'; $TICKET_OUT_1 = Join-Path $TICKET_RUN_1 'output'; $TICKET_OUT_2 = Join-Path $TICKET_RUN_2 'output'; $TICKET_CACHE_1 = Join-Path $TICKET_RUN_1 'cache-v1.db'; $TICKET_CACHE_2 = Join-Path $TICKET_RUN_2 'cache-v1.db'
New-Item -ItemType Directory -Force -Path $CT_RUN_1,$CT_RUN_2,$FRAUD_RUN_1,$FRAUD_RUN_2,$SMS_RUN_1,$SMS_RUN_2,$TICKET_RUN_1,$TICKET_RUN_2 | Out-Null
dotnet restore (Join-Path $SEQDOC_ROOT 'SeqDoc.slnx'); dotnet build (Join-Path $SEQDOC_ROOT 'SeqDoc.slnx') -c Release
dotnet restore $CT_TARGET; dotnet build $CT_TARGET -c Release -f $CT_FRAMEWORK
dotnet restore $FRAUD_TARGET; dotnet build $FRAUD_TARGET -c Release
dotnet restore $SMS_TARGET; dotnet build $SMS_TARGET -c Release
dotnet restore $TICKET_TARGET; dotnet build $TICKET_TARGET -c Release -f $TICKET_FRAMEWORK
```

Run both bound outputs and caches for every lane. Ticket deliberately has no `--config` option (HTTP roots only):

```powershell
dotnet run --project (Join-Path $SEQDOC_ROOT 'src\SeqDoc.Cli') --configuration Release --no-build -- analyze $CT_TARGET --repository-root $CT_REPO --config $CT_CONFIG --configuration Release --framework $CT_FRAMEWORK --cache $CT_CACHE_1 --output $CT_OUT_1
dotnet run --project (Join-Path $SEQDOC_ROOT 'src\SeqDoc.Cli') --configuration Release --no-build -- analyze $CT_TARGET --repository-root $CT_REPO --config $CT_CONFIG --configuration Release --framework $CT_FRAMEWORK --cache $CT_CACHE_2 --output $CT_OUT_2
dotnet run --project (Join-Path $SEQDOC_ROOT 'src\SeqDoc.Cli') --configuration Release --no-build -- analyze $FRAUD_TARGET --repository-root $FRAUD_REPO --config $FRAUD_CONFIG --configuration Release --framework $FRAUD_FRAMEWORK --cache $FRAUD_CACHE_1 --output $FRAUD_OUT_1
dotnet run --project (Join-Path $SEQDOC_ROOT 'src\SeqDoc.Cli') --configuration Release --no-build -- analyze $FRAUD_TARGET --repository-root $FRAUD_REPO --config $FRAUD_CONFIG --configuration Release --framework $FRAUD_FRAMEWORK --cache $FRAUD_CACHE_2 --output $FRAUD_OUT_2
dotnet run --project (Join-Path $SEQDOC_ROOT 'src\SeqDoc.Cli') --configuration Release --no-build -- analyze $SMS_TARGET --repository-root $SMS_REPO --config $SMS_CONFIG --configuration Release --framework $SMS_FRAMEWORK --cache $SMS_CACHE_1 --output $SMS_OUT_1
dotnet run --project (Join-Path $SEQDOC_ROOT 'src\SeqDoc.Cli') --configuration Release --no-build -- analyze $SMS_TARGET --repository-root $SMS_REPO --config $SMS_CONFIG --configuration Release --framework $SMS_FRAMEWORK --cache $SMS_CACHE_2 --output $SMS_OUT_2
dotnet run --project (Join-Path $SEQDOC_ROOT 'src\SeqDoc.Cli') --configuration Release --no-build -- analyze $TICKET_TARGET --repository-root $TICKET_REPO --configuration Release --framework $TICKET_FRAMEWORK --cache $TICKET_CACHE_1 --output $TICKET_OUT_1
dotnet run --project (Join-Path $SEQDOC_ROOT 'src\SeqDoc.Cli') --configuration Release --no-build -- analyze $TICKET_TARGET --repository-root $TICKET_REPO --configuration Release --framework $TICKET_FRAMEWORK --cache $TICKET_CACHE_2 --output $TICKET_OUT_2
```

CT target is `CreditTransferWeb\CreditTransfer.csproj`, Fraud target is exactly `FraudManagement.sln` (not
WindowsService), SMS target is `Source\LP.SMSGateway.WindowsHost\LP.SMSGateway.WindowsHost.csproj`, and Ticket target is
`TicketReservation.Api\TicketReservation.Api.csproj`; each uses its matching repository, example config, and accepted
exact framework. The comparison/link/budget script sorts manifest paths, compares both manifests and every listed file
SHA256, resolves Markdown links, and fails on unequal hashes, missing links/Markdown, or `.Length > 45000`, using the
metrics above. Render with `npx --yes @mermaid-js/mermaid-cli@11.16.0 -i <file.mmd> -o <file.svg>` for every run-1 file.

Audit both runs with this executable deterministic PowerShell block. It sorts relative paths, compares manifests and
every file byte/hash, checks same-stem Markdown, resolves non-HTTP relative Markdown links, reports Mermaid metrics,
and fails on any diagram over 45,000 UTF-16/.NET characters:

```powershell
$runs = @(@{Name='CT'; One=$CT_OUT_1; Two=$CT_OUT_2},@{Name='Fraud'; One=$FRAUD_OUT_1; Two=$FRAUD_OUT_2},@{Name='SMS'; One=$SMS_OUT_1; Two=$SMS_OUT_2},@{Name='Ticket'; One=$TICKET_OUT_1; Two=$TICKET_OUT_2})
$hash = { param($p) (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest = { param($root) @(Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object { [pscustomobject]@{ Path=([IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')); Hash=& $hash $_.FullName } } | Sort-Object Path) }
foreach($run in $runs) {
  $a=& $manifest $run.One; $b=& $manifest $run.Two
  if(($a | ConvertTo-Json -Compress) -ne ($b | ConvertTo-Json -Compress)){ throw "$($run.Name): manifests differ" }
  foreach($entry in $a) { $p1=Join-Path $run.One $entry.Path; $p2=Join-Path $run.Two $entry.Path; if((& $hash $p1) -ne (& $hash $p2) -or -not [System.Linq.Enumerable]::SequenceEqual([IO.File]::ReadAllBytes($p1),[IO.File]::ReadAllBytes($p2))){ throw "$($run.Name): file differs: $($entry.Path)" } }
  foreach($mmd in Get-ChildItem -LiteralPath $run.One -File -Recurse -Filter '*.mmd') {
    $md=[IO.Path]::ChangeExtension($mmd.FullName,'.md'); if(-not (Test-Path -LiteralPath $md)){ throw "$($run.Name): missing Markdown: $md" }
    $text=[IO.File]::ReadAllText($mmd.FullName); $chars=$text.Length; if($chars -gt 45000){ throw "$($run.Name): budget: $mmd" }
    $messages=([regex]::Matches($text,'(?m)^\s*[^\r\n:]+->>[^\r\n:]+:')).Count; $participants=([regex]::Matches($text,'(?m)^\s*participant\s+')).Count; $fragments=([regex]::Matches($text,'(?m)^\s*(?:alt|opt|loop|par|critical|break)\b')).Count; "$($run.Name)|$($mmd.Name)|$chars/$messages/$participants/$fragments"
  }
  foreach($md in Get-ChildItem -LiteralPath $run.One -File -Recurse -Filter '*.md') { foreach($match in [regex]::Matches([IO.File]::ReadAllText($md.FullName),'\[[^]]*\]\(([^)]+)\)')) { $link=$match.Groups[1].Value; if($link -notmatch '^(?:https?|mailto):' -and $link -notmatch '^#'){ $target=Join-Path $md.DirectoryName ($link -split '#')[0]; if(-not (Test-Path -LiteralPath $target)){ throw "$($run.Name): broken link in $($md.Name): $link" } } } }
}
```

Render every run-1 Mermaid file with the pinned CLI and require its SVG:

```powershell
foreach($out in @($CT_OUT_1,$FRAUD_OUT_1,$SMS_OUT_1,$TICKET_OUT_1)){ foreach($mmd in Get-ChildItem -LiteralPath $out -File -Recurse -Filter '*.mmd'){ $svg=[IO.Path]::ChangeExtension($mmd.FullName,'.svg'); npx --yes @mermaid-js/mermaid-cli@11.16.0 -i $mmd.FullName -o $svg; if($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $svg)){ throw "Mermaid render failed: $mmd" } } }
```

`P2D`, `I21PUB`, and `docs/project/execution.json` are parent-orchestration records only and will not enter Issue #21
publication. The actual publication candidate will be reconstructed in a new clean branch containing only the Issue
#21 allowlist.
