require 'fileutils'
require 'json'
require 'net/http'
require 'rbconfig'
require 'socket'
require 'tmpdir'

SOLUTION = 'Ehcp.slnx'
# Public Cosmos emulator key (same default as CosmosSettings); override with Cosmos__Key.
EMULATOR_KEY = 'C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=='.freeze
BASE_URL = ENV.fetch('Ehcp__BaseUrl', 'http://localhost:5100')
API_DIR = 'src/Ehcp.ApiFunctions'
PROCESSING_DIR = 'src/Ehcp.Processing'
MOCK_GRAPH_DIR = 'src/Ehcp.MockGraph'
MOCK_EXTRACTION_DIR = 'src/Ehcp.MockExtraction'
MOCK_GRAPH_PORT = 7101
MOCK_EXTRACTION_PORT = 7102
MOCK_GRAPH_URL = "http://localhost:#{MOCK_GRAPH_PORT}".freeze
MOCK_EXTRACTION_URL = "http://localhost:#{MOCK_EXTRACTION_PORT}".freeze
# Placeholder Graph identity; the mock Graph service ignores the credentials and tenant.
MOCK_GRAPH_ID = '00000000-0000-0000-0000-000000000000'.freeze
LIVINGDOC_CONFIG = 'tests/Ehcp.Specs/livingdocgen.json'

def dotnet(*args)
  sh 'dotnet', *args
end

# Azure sandbox. Must already exist; override with EHCP_RESOURCE_GROUP.
RESOURCE_GROUP = ENV.fetch('EHCP_RESOURCE_GROUP', 'jamie-lupton-sandbox-rg')
AZ_DEPLOYMENT = 'ehcp'
# Publish output and zips kept after each deploy, for manual config-zip runs.
DEPLOY_ARTIFACTS = 'artifacts/deploy'
# Function App output name => project. Each app is deployed separately.
AZURE_APPS = {
  'processingAppName' => 'src/Ehcp.Processing',
  'apiAppName' => 'src/Ehcp.ApiFunctions',
  'mockGraphAppName' => 'src/Ehcp.MockGraph',
  'mockExtractionAppName' => 'src/Ehcp.MockExtraction'
}.freeze

def az(*args)
  sh "az #{args.join(' ')}"
end

def az_capture(*args)
  # String form runs through the shell so the Windows az.cmd shim resolves.
  output = IO.popen("az #{args.join(' ')}", err: File::NULL, &:read)
  abort "az #{args.first(2).join(' ')} failed." unless $?.success?
  output.strip
end

def deployment_outputs
  JSON.parse(az_capture('deployment', 'group', 'show', '--resource-group', RESOURCE_GROUP,
    '--name', AZ_DEPLOYMENT, '--query', 'properties.outputs', '--output', 'json')).transform_values { |v| v['value'] }
end


def configure_environment
  {
    'Cosmos__Endpoint' => 'https://localhost:8081',
    'Cosmos__Key' => EMULATOR_KEY,
    'Cosmos__Database' => 'ehcp-team-shared-1',
    'Cosmos__Initialize' => 'true',
    'ASPNETCORE_ENVIRONMENT' => 'Development',
    'Ehcp__BaseUrl' => BASE_URL,
    'AzureWebJobsStorage' => 'UseDevelopmentStorage=true',
    'ProcessingQueueName' => 'ehcp-processing',
    'Processing__Graph__Mode' => 'Live',
    'Processing__Graph__BaseUrl' => MOCK_GRAPH_URL,
    'Processing__Graph__LoginBaseUrl' => MOCK_GRAPH_URL,
    'Processing__Graph__TenantId' => MOCK_GRAPH_ID,
    'Processing__Graph__ClientId' => MOCK_GRAPH_ID,
    'Processing__Graph__ClientSecret' => 'local-mock-secret',
    'Processing__Graph__Mailboxes__ehcp-team-shared-1' => 'mock-mailbox@localhost',
    'Processing__Extraction__Mode' => 'Live',
    'Processing__Extraction__Endpoint' => "#{MOCK_EXTRACTION_URL}/api/extract",
    'MockGraph__BaseUrl' => MOCK_GRAPH_URL,
    'MockExtraction__BaseUrl' => MOCK_EXTRACTION_URL
  }.each { |name, value| ENV[name] ||= value }
  ENV['EHCP_TEST_COSMOS_KEY'] ||= ENV['Cosmos__Key']
  ENV['EHCP_TEST_COSMOS_ENDPOINT'] ||= ENV['Cosmos__Endpoint']
end

# Any HTTP response counts as ready, so unknown routes (404) on the mocks still pass.
def http_responding?(url)
  Net::HTTP.get_response(URI(url))
  true
rescue SystemCallError, IOError
  false
end

def wait_for_http(pid, name, url, timeout: 60)
  deadline = Time.now + timeout
  until http_responding?(url)
    abort "#{name} process exited before becoming ready." if Process.waitpid(pid, Process::WNOHANG)
    abort "#{name} not ready after #{timeout}s." if Time.now > deadline
    sleep 1
  end
end

AZURITE_PORTS = [10000, 10001, 10002].freeze

def port_open?(port)
  TCPSocket.new('127.0.0.1', port).close
  true
rescue SystemCallError, IOError
  false
end

# Starts Azurite (blob, queue, table) with the flags the Specs hook expects, unless all ports already answer.
def start_azurite(timeout: 60)
  open = AZURITE_PORTS.map { |port| port_open?(port) }
  return nil if open.all?
  abort 'Azurite is partially running. Stop the existing instance and rerun.' if open.any?

  args = ['--silent', '--skipApiVersionCheck',
          '--blobHost', '127.0.0.1', '--blobPort', '10000',
          '--queueHost', '127.0.0.1', '--queuePort', '10001',
          '--tableHost', '127.0.0.1', '--tablePort', '10002',
          '--location', File.join(Dir.tmpdir, 'ehcp-specs-azurite')]
  pid = if Gem.win_platform?
          Process.spawn('cmd', '/d', '/s', '/c', 'azurite', *args)
        else
          Process.spawn('azurite', *args)
        end

  deadline = Time.now + timeout
  until AZURITE_PORTS.all? { |port| port_open?(port) }
    abort 'Azurite exited before becoming ready.' if Process.waitpid(pid, Process::WNOHANG)
    abort "Azurite not ready after #{timeout}s." if Time.now > deadline
    sleep 1
  end
  pid
end

def stop_process(pid)
  if Gem.win_platform?
    system('taskkill', '/T', '/F', '/PID', pid.to_s, out: File::NULL, err: File::NULL)
  else
    Process.kill('TERM', pid)
  end
  Process.wait(pid)
rescue Errno::ESRCH, Errno::ECHILD
  nil
end

# The Processing function host (queue trigger) has no HTTP endpoint to poll for readiness, so just
# confirm it didn't crash immediately (missing func CLI, bad project, etc.) and give it a moment to
# start its worker process; Specs' own "wait for processing status" step already tolerates the rest.
def wait_for_processing(pid, grace: 6)
  deadline = Time.now + grace
  until Time.now > deadline
    abort 'Processing function host exited before becoming ready.' if Process.waitpid(pid, Process::WNOHANG)
    sleep 1
  end
end

# Starts the API, Processing host and both mock services. Returns { name => pid }.
def start_services
  {
    'API' => Process.spawn('func', 'start', '--dotnet-isolated', '--port', URI(BASE_URL).port.to_s, chdir: API_DIR),
    'Processing' => Process.spawn('func', 'start', chdir: PROCESSING_DIR),
    'MockGraph' => Process.spawn('func', 'start', '--dotnet-isolated', '--port', MOCK_GRAPH_PORT.to_s, chdir: MOCK_GRAPH_DIR),
    'MockExtraction' => Process.spawn('func', 'start', '--dotnet-isolated', '--port', MOCK_EXTRACTION_PORT.to_s, chdir: MOCK_EXTRACTION_DIR)
  }
end

def wait_for_services(pids)
  wait_for_http(pids['API'], 'API', "#{BASE_URL}/")
  wait_for_http(pids['MockGraph'], 'MockGraph', MOCK_GRAPH_URL)
  wait_for_http(pids['MockExtraction'], 'MockExtraction', MOCK_EXTRACTION_URL)
  wait_for_processing(pids['Processing'])
end

def stop_services(pids)
  pids.each_value { |pid| stop_process(pid) }
end

# Blocks until any service exits, then aborts so the caller's ensure stops the rest.
def watch_services(pids)
  loop do
    pids.each do |name, pid|
      abort "#{name} exited unexpectedly." if Process.waitpid(pid, Process::WNOHANG)
    end
    sleep 1
  end
end

desc 'Set environment variables'
task :env do
  configure_environment
end

desc 'Restore packages'
task restore: :env do
  dotnet 'restore', SOLUTION
end

desc 'Build solution'
task build: :restore do
  dotnet 'build', SOLUTION, '--no-restore'
end

desc 'Run unit and API tests'
task unit: :build do
  dotnet 'test', 'tests/Ehcp.Tests', '--no-build', '--logger', 'trx', '--results-directory', 'tests/Ehcp.Tests/TestResults'
end

desc 'Run the API (Azure Functions, port 5100) in the foreground'
task run: :build do
  Dir.chdir(API_DIR) { sh 'func', 'start', '--dotnet-isolated', '--port', URI(BASE_URL).port.to_s }
end

desc 'Run the Processing function host in the foreground'
task processing: :build do
  configure_environment
  Dir.chdir(PROCESSING_DIR) { sh 'func', 'start' }
end

desc 'Start API, Processing host, MockGraph and MockExtraction in the foreground; Ctrl+C stops them'
task up: :build do
  pids = start_services
  begin
    wait_for_services(pids)
    puts "Services ready: API #{BASE_URL}, MockGraph #{MOCK_GRAPH_URL}, MockExtraction #{MOCK_EXTRACTION_URL}. Ctrl+C to stop."
    watch_services(pids)
  rescue Interrupt
    nil
  ensure
    stop_services(pids)
  end
end

def clear_trx_results
  Dir.glob('tests/Ehcp.Specs/TestResults/*.trx').each { |file| File.delete(file) }
end

desc 'Run Reqnroll specs against already running services (see rake up)'
task specs: [:build, :env] do
  ENV['EHCP_SKIP_HOST_BUILD'] = '1'
  clear_trx_results
  dotnet 'test', 'tests/Ehcp.Specs', '--no-build', '--settings', '.runsettings',
    '--logger', 'trx', '--results-directory', 'tests/Ehcp.Specs/TestResults'
end

desc 'Run only @wip-tagged Reqnroll specs against already running services (see rake up)'
task wip: [:build, :env] do
  ENV['EHCP_SKIP_HOST_BUILD'] = '1'
  clear_trx_results
  dotnet 'test', 'tests/Ehcp.Specs', '--no-build', '--settings', '.runsettings', '--filter', 'Category=wip',
    '--logger', 'trx', '--results-directory', 'tests/Ehcp.Specs/TestResults'
end

desc 'Build, run unit tests, start Azurite, run Reqnroll specs (hook starts API and hosts), stop Azurite'
task go: :unit do
  azurite = start_azurite
  begin
    Rake::Task[:specs].invoke
  ensure
    stop_process(azurite) if azurite
  end
end

# Packs each Function App with func pack and zip-deploys it. Outputs come from the last Bicep deployment.
def publish_function_apps(outputs, only: nil)
  FileUtils.mkdir_p(DEPLOY_ARTIFACTS)
  # func pack reads the worker runtime from here; there is no local.settings.json in these projects.
  ENV['FUNCTIONS_WORKER_RUNTIME'] = 'dotnet-isolated'
  AZURE_APPS.each do |output, project|
    next if only && !only.include?(output)
    # func pack writes <ProjectName>.zip into the --output folder; move it to a stable name.
    pack_dir = File.join(DEPLOY_ARTIFACTS, output)
    FileUtils.mkdir_p(pack_dir)
    # String form so the Windows func.cmd shim resolves.
    sh "func pack #{project} --output #{pack_dir}"
    zip = File.join(DEPLOY_ARTIFACTS, "#{output}.zip")
    FileUtils.mv(Dir.glob(File.join(pack_dir, '*.zip')).first, zip)
    FileUtils.rm_rf(pack_dir)
    puts "Zip for #{output}: #{File.expand_path(zip)}"
    # Freshly created apps can time out on the SCM endpoint, so retry before failing.
    3.times do |attempt|
      az 'functionapp', 'deployment', 'source', 'config-zip', '--resource-group', RESOURCE_GROUP,
        '--name', outputs[output], '--src', zip, '--output', 'none'
      break
    rescue RuntimeError
      raise if attempt == 2
      puts "Zip deploy to #{outputs[output]} failed; retrying in 30s (attempt #{attempt + 2}/3)."
      sleep 30
    end
  end
end

desc 'Deploy the Azure sandbox into the existing resource group: Bicep infrastructure, then publish the four Function Apps'
task :deploy do
  az 'deployment', 'group', 'create', '--resource-group', RESOURCE_GROUP, '--name', AZ_DEPLOYMENT,
    '--template-file', 'infra/main.bicep', '--output', 'none'
  outputs = deployment_outputs
  publish_function_apps(outputs)
  puts "Deployed. API: #{outputs['apiUrl']}"
end

# Short names accepted by publish_apps; keys are the AZURE_APPS output names.
PUBLISH_APP_NAMES = {
  'processing' => 'processingAppName',
  'api' => 'apiAppName',
  'mock-graph' => 'mockGraphAppName',
  'mock-extraction' => 'mockExtractionAppName'
}.freeze

desc 'Republish Function Apps only. rake "publish_apps[api]" for one; no argument publishes all four. No Bicep.'
task :publish_apps, [:app] do |_, args|
  only = nil
  if args[:app]
    only = [PUBLISH_APP_NAMES.fetch(args[:app]) do
      abort "Unknown app '#{args[:app]}'. Use one of: #{PUBLISH_APP_NAMES.keys.join(', ')}."
    end]
  end
  outputs = deployment_outputs
  publish_function_apps(outputs, only: only)
  puts "Published. API: #{outputs['apiUrl']}"
end

def run_remote_specs(*extra_args)
  outputs = deployment_outputs
  ENV['EHCP_TARGET'] = 'remote'
  ENV['Ehcp__BaseUrl'] = outputs['apiUrl']
  ENV['MockGraph__BaseUrl'] = outputs['mockGraphUrl']
  ENV['MockExtraction__BaseUrl'] = outputs['mockExtractionUrl']
  ENV['Cosmos__Endpoint'] = outputs['cosmosEndpoint']
  ENV['Cosmos__Database'] = outputs['databaseName']
  ENV['Cosmos__Key'] = az_capture('cosmosdb', 'keys', 'list', '--name', outputs['cosmosAccountName'],
    '--resource-group', RESOURCE_GROUP, '--query', 'primaryMasterKey', '--output', 'tsv')
  ENV['AzureWebJobsStorage'] = az_capture('storage', 'account', 'show-connection-string',
    '--name', outputs['storageAccountName'], '--resource-group', RESOURCE_GROUP,
    '--query', 'connectionString', '--output', 'tsv')
  ENV['ProcessingQueueName'] = outputs['queueName']
  ENV['EHCP_TEST_COSMOS_KEY'] = ENV['Cosmos__Key']
  ENV['EHCP_TEST_COSMOS_ENDPOINT'] = ENV['Cosmos__Endpoint']
  warn 'WARNING: remote specs delete all documents in the sandbox email and case containers, scenario by scenario.'
  Dir.glob('tests/Ehcp.Specs/TestResults/*.trx').each { |f| File.delete(f) }
  with_livingdoc_title("Ehcp Specs - remote: #{URI(outputs['apiUrl']).host}") do
    dotnet 'test', 'tests/Ehcp.Specs', '--no-build', *extra_args,
      '--logger', 'trx', '--results-directory', 'tests/Ehcp.Specs/TestResults'
  end
end

# LivingDocGen reads its title from livingdocgen.json at report time. Set it for one run, then restore the file.
def with_livingdoc_title(title)
  original = File.read(LIVINGDOC_CONFIG)
  config = JSON.parse(original)
  config['title'] = title
  File.write(LIVINGDOC_CONFIG, JSON.pretty_generate(config))
  yield
ensure
  File.write(LIVINGDOC_CONFIG, original) if original
end

desc 'Run Reqnroll specs against the deployed Azure sandbox (run rake deploy first)'
task test_remote: :build do
  run_remote_specs
end

desc 'Run only @wip-tagged specs against the deployed Azure sandbox'
task test_remote_wip: :build do
  run_remote_specs('--filter', 'Category=wip')
end

task default: :go
