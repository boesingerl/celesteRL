export DOTNET_ROOT=/usr/lib/dotnet/
for i in $(seq 1 8);
do
    sleep 1
    ../../../Celeste &
done

