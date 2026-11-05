require 'net/http'
require 'rbconfig'

SOLUTION = 'Ehcp.slnx'
# Public Cosmos emulator key (same default as CosmosSettings); override with Cosmos__Key.
EMULATOR_KEY = 'C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=='.freeze
BASE_URL = ENV.fetch('Ehcp__BaseUrl', 'http://localhost:5100')
PROCESSING_DIR = 'src/Ehcp.Processing'
MOCK_GRAPH_DIR = 'src/Ehcp.MockGraph'
MOCK_EXTRACTION_DIR = 'src/Ehcp.MockExtraction'
MOCK_GRAPH_PORT = 7101
MOCK_EXTRACTION_PORT = 7102
MOCK_GRAPH_URL = "http://localhost:#{MOCK_GRAPH_PORT}".freeze
MOCK_EXTRACTION_URL = "http://localhost:#{MOCK_EXTRACTION_PORT}".freeze
# Placeholder Graph identity; the mock Graph service ignores the credentials and tenant.
MOCK_GRAPH_ID = '00000000-0000-0000-0000-000000000000'.freeze

def dotnet(*args)
  sh 'dotnet', *args
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
    'API' => Process.spawn('dotnet', 'run', '--no-build', '--project', 'src/Ehcp.Api'),
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
  dotnet 'test', 'tests/Ehcp.Tests', '--no-build'
end

desc 'Run API in the foreground'
task run: :build do
  dotnet 'run', '--no-build', '--project', 'src/Ehcp.Api'
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

desc 'Run Reqnroll specs against already running services (see rake up)'
task specs: :env do
  dotnet 'test', 'tests/Ehcp.Specs', '--no-build', '--logger', 'trx', '--results-directory', 'tests/Ehcp.Specs/TestResults'
end

desc 'Run only @wip-tagged Reqnroll specs against already running services (see rake up)'
task wip: :env do
  dotnet 'test', 'tests/Ehcp.Specs', '--no-build', '--filter', 'Category=wip',
    '--logger', 'trx', '--results-directory', 'tests/Ehcp.Specs/TestResults'
end

desc 'Build, run unit tests, start services (rake up), run Reqnroll specs, stop services'
task go: :unit do
  pids = start_services
  begin
    wait_for_services(pids)
    Rake::Task[:specs].invoke
  ensure
    stop_services(pids)
  end
end

task default: :go
